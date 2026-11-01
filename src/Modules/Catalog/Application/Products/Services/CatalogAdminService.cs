using System.Globalization;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Catalog.Application.Products.Services;

public sealed class CatalogAdminService(
    IProductRepository products,
    IBrandRepository brands,
    ICategoryRepository categories,
    ICategoryProductRepository categoryProducts,
    IAuditTrail audit,
    ICatalogChangeNotifier notifier,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : ICatalogAdminService
{
    private const string TargetType = "Product";

    public async Task<Result<long>> CreateProductAsync(CreateProductCommand command, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);

        if (await ReferencesMissingAsync(command.BrandId, command.PrimaryCategoryId, ct) is { } missing)
            return Result.Failure<long>(missing);

        Product product;
        try
        {
            product = Product.CreateDraft(
                ids.Generate(), command.BrandId, command.PrimaryCategoryId, command.Title,
                command.Slug, command.ShortDescription, command.Description, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure<long>(Domain(exception));
        }

        if (await products.GetBySlugAsync(product.Slug, ct) is not null)
            return Result.Failure<long>(SlugDuplicate(product.Slug));

        await products.AddAsync(product, ct);
        audit.Record(actor.ToAudit(), "product.created", TargetType, Key(product.Id),
            before: null, after: Snapshot(product));

        if (await unitOfWork.TrySaveChangesAsync(ct) == SaveOutcome.UniqueViolation)
            return Result.Failure<long>(SlugDuplicate(product.Slug));

        // Display only. A draft is not sellable, so no cart anywhere could be holding it.
        await notifier.DisplayChangedAsync(ct);
        return Result.Success(product.Id);
    }

    public async Task<Result> UpdateProductAsync(UpdateProductCommand command, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);

        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null) return Result.Failure(ProductNotFound());

        if (await ReferencesMissingAsync(command.BrandId, command.PrimaryCategoryId, ct) is { } missing)
            return Result.Failure(missing);

        var before = Snapshot(product);
        var placementChanged = product.BrandId != command.BrandId
            || product.PrimaryCategoryId != command.PrimaryCategoryId;

        try
        {
            product.UpdateDetails(
                command.BrandId, command.PrimaryCategoryId, command.Title,
                command.ShortDescription, command.Description, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(Domain(exception));
        }

        audit.Record(actor.ToAudit(), "product.updated", TargetType, Key(product.Id),
            before, Snapshot(product));
        await unitOfWork.SaveChangesAsync(ct);

        await NotifyAsync(product, placementChanged, ct);
        return Result.Success();
    }

    public async Task<Result> SetSecondaryCategoriesAsync(long productId, IReadOnlyList<long> categoryIds,
        AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(categoryIds);
        ArgumentNullException.ThrowIfNull(actor);

        var product = await products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(ProductNotFound());

        // The primary category is set through UpdateProductAsync. Including it here would make
        // the same fact true in two places, which is the kind of thing that drifts.
        var wanted = categoryIds.Where(id => id != product.PrimaryCategoryId).Distinct().ToArray();
        foreach (var categoryId in wanted)
        {
            if (await categories.GetByIdAsync(categoryId, ct) is null)
                return Result.Failure(CategoryNotFound(categoryId));
        }

        var current = await categoryProducts.ListCategoryIdsForProductAsync(productId, ct);
        var before = current.OrderBy(id => id).ToArray();

        foreach (var categoryId in before.Except(wanted))
            await categoryProducts.RemoveIfPresentAsync(categoryId, productId, ct);

        foreach (var categoryId in wanted.Except(before))
        {
            await categoryProducts.AddIfAbsentAsync(
                CategoryProduct.Create(categoryId, productId, clock.UtcNow, actor.UserId), ct);
        }

        audit.Record(actor.ToAudit(), "product.categories_changed", TargetType, Key(productId),
            new { Categories = before }, new { Categories = wanted.OrderBy(id => id).ToArray() });
        await unitOfWork.SaveChangesAsync(ct);

        // Where a product is filed changes which listings show it, not whether it can be bought.
        await notifier.DisplayChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result<long>> AddVariantAsync(AddVariantCommand command, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);

        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null) return Result.Failure<long>(ProductNotFound());

        var variantId = ids.Generate();
        var sku = string.IsNullOrWhiteSpace(command.Sku)
            ? $"SKU-{variantId.ToString(CultureInfo.InvariantCulture)}"
            : command.Sku;

        // Pre-checked so the admin reads a conflict; still re-checked by the save below, because
        // another admin can claim the SKU in the gap between the two.
        if (await products.SkuExistsAsync(sku, ct))
            return Result.Failure<long>(SkuDuplicate(sku));

        try
        {
            product.AddVariant(variantId, sku, command.Attributes, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure<long>(Domain(exception));
        }

        audit.Record(actor.ToAudit(), "product.variant_added", TargetType, Key(product.Id),
            before: null, after: new { VariantId = variantId, Sku = sku, command.Attributes });

        if (await unitOfWork.TrySaveChangesAsync(ct) == SaveOutcome.UniqueViolation)
            return Result.Failure<long>(SkuDuplicate(sku));

        await NotifyAsync(product, sellabilityChanged: true, ct);
        return Result.Success(variantId);
    }

    public async Task<Result> AddIdentifierAsync(long productId, IdentifierInput identifier,
        AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(actor);

        var product = await products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(ProductNotFound());

        var identifierId = ids.Generate();
        try
        {
            product.AddIdentifier(
                identifierId, identifier.Type, identifier.Value, identifier.SourceDomain, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(Domain(exception));
        }

        audit.Record(actor.ToAudit(), "product.identifier_added", TargetType, Key(productId),
            before: null, after: new { identifier.Type, identifier.Value, identifier.SourceDomain });

        // The identifier indexes span every product, so only the database can answer whether this
        // code is already someone else's.
        if (await unitOfWork.TrySaveChangesAsync(ct) == SaveOutcome.UniqueViolation)
            return Result.Failure(IdentifierDuplicate(identifier.Value));

        await notifier.DisplayChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveIdentifierAsync(long productId, long identifierId, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var product = await products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(ProductNotFound());

        var before = product.Identifiers.FirstOrDefault(identifier => identifier.Id == identifierId);
        try
        {
            product.RemoveIdentifier(identifierId, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(Domain(exception));
        }

        audit.Record(actor.ToAudit(), "product.identifier_removed", TargetType, Key(productId),
            before is null ? null : new { before.Type, before.Value, before.SourceDomain }, after: null);
        await unitOfWork.SaveChangesAsync(ct);

        await notifier.DisplayChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetProductActiveAsync(long productId, bool active, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var product = await products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(ProductNotFound());

        var before = Snapshot(product);
        try
        {
            // byAdmin, always: this method is only reachable by an admin, and the flag is what
            // stops the next import run from undoing what they just decided.
            if (active) product.Activate(byAdmin: true, clock.UtcNow);
            else product.Deactivate(byAdmin: true, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(Domain(exception));
        }

        audit.Record(actor.ToAudit(), active ? "product.activated" : "product.deactivated",
            TargetType, Key(productId), before, Snapshot(product));
        await unitOfWork.SaveChangesAsync(ct);

        await notifier.SellabilityChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetVariantActiveAsync(long productId, long variantId, bool active,
        AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var product = await products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(ProductNotFound());

        var before = Snapshot(product);
        try
        {
            product.SetVariantActive(variantId, active, byAdmin: true, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(Domain(exception));
        }

        audit.Record(actor.ToAudit(), active ? "product.variant_activated" : "product.variant_deactivated",
            TargetType, Key(productId), before, Snapshot(product));
        await unitOfWork.SaveChangesAsync(ct);

        await notifier.SellabilityChangedAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// A draft has never been sellable, so nothing a cart holds can be affected by a change to
    /// one however sweeping it is — those get a display notification and leave carts alone.
    /// </summary>
    private Task NotifyAsync(Product product, bool sellabilityChanged, CancellationToken ct) =>
        sellabilityChanged && product.Status != ProductStatus.Draft
            ? notifier.SellabilityChangedAsync(ct)
            : notifier.DisplayChangedAsync(ct);

    private async Task<Error?> ReferencesMissingAsync(long brandId, long categoryId, CancellationToken ct)
    {
        if (!await brands.ExistsAsync(brandId, ct)) return BrandNotFound(brandId);
        if (await categories.GetByIdAsync(categoryId, ct) is null) return CategoryNotFound(categoryId);
        return null;
    }

    private static string Key(long productId) => productId.ToString(CultureInfo.InvariantCulture);

    private static object Snapshot(Product product) => new
    {
        product.BrandId,
        product.PrimaryCategoryId,
        product.Title,
        product.Slug,
        Status = product.Status.ToString(),
        product.DeactivatedByAdmin,
        product.Mpn,
        Variants = product.Variants
            .Select(variant => new { variant.Id, Sku = variant.Sku.Value, variant.IsActive })
            .OrderBy(variant => variant.Id).ToArray()
    };

    private static Error Domain(DomainException exception) => new(exception.Code, exception.Message);

    private static Error ProductNotFound() => new("PRODUCT_NOT_FOUND", "Product was not found.", 404);

    private static Error BrandNotFound(long brandId) =>
        new("BRAND_NOT_FOUND", $"Brand '{brandId.ToString(CultureInfo.InvariantCulture)}' was not found.", 404);

    private static Error CategoryNotFound(long categoryId) =>
        new("CATEGORY_NOT_FOUND", $"Category '{categoryId.ToString(CultureInfo.InvariantCulture)}' was not found.", 404);

    private static Error SlugDuplicate(string slug) =>
        new("PRODUCT_SLUG_DUPLICATE", $"The slug '{slug}' is already in use.", 409);

    private static Error SkuDuplicate(string sku) =>
        new("VARIANT_SKU_DUPLICATE", $"SKU '{sku}' is already used by another variant.", 409);

    private static Error IdentifierDuplicate(string value) =>
        new("PRODUCT_IDENTIFIER_DUPLICATE", $"'{value}' already identifies another product.", 409);
}
