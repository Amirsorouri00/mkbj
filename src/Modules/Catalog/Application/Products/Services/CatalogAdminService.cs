using System.Globalization;
using EwaysVNext.Core.Application.FileTypeDetection;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Catalog.Application.Products.Services;

public sealed class CatalogAdminService(
    IProductRepository products,
    ICatalogMediaStore media,
    IFileTypeDetector fileTypes,
    IOptions<CatalogMediaOptions> mediaOptions,
    ILogger<CatalogAdminService> logger,
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

    /// <summary>
    /// Beyond this an admin is describing a query rather than a selection, and one save holding
    /// thousands of aggregates holds its locks far too long.
    /// </summary>
    private const int MaxBulkProducts = 500;

    /// <summary>
    /// What a browser will render without a plugin and what the storefront is built around.
    /// A denylist would be endless; this is the whole set the seller publishes.
    /// </summary>
    private static readonly HashSet<FileType> AllowedImageTypes = [FileType.Jpeg, FileType.Png, FileType.Webp];

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

    public async Task<Result<long>> UploadMediaAsync(UploadMediaCommand command, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);

        var limit = mediaOptions.Value.MaxBytes;
        if (command.Length > limit)
        {
            // Refused from the declared length, before a byte is read: reading first is how a
            // size limit becomes a way to fill the disk.
            return Result.Failure<long>(new Error(
                "MEDIA_TOO_LARGE",
                $"An image cannot exceed {limit.ToString(CultureInfo.InvariantCulture)} bytes.",
                413));
        }

        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null) return Result.Failure<long>(ProductNotFound());

        var (detected, replayed) = await SniffAsync(command.Content, ct);
        if (!AllowedImageTypes.Contains(detected.FileType))
        {
            // Sniffed from the bytes, never from a declared content type or a file name: both
            // are the uploader's claim, and an executable renamed to .png would pass either.
            return Result.Failure<long>(new Error(
                "MEDIA_TYPE_UNSUPPORTED", "An image must be a JPEG, a PNG or a WebP.", 415));
        }

        var mediaId = ids.Generate();
        // GetExtension already carries the dot (".png"), so this does not add one.
        var objectKey = $"products/{Key(command.ProductId)}/{Key(mediaId)}{detected.Extension}";
        var sortOrder = command.SortOrder
            ?? (product.Media.Count == 0 ? 0 : product.Media.Max(image => image.SortOrder) + 1);

        await media.PutAsync(objectKey, replayed, detected.MimeType!, ct);

        try
        {
            product.AddMedia(mediaId, objectKey, sortOrder, command.Alt, null, command.Rights, clock.UtcNow);
            audit.Record(actor.ToAudit(), "product.media_added", TargetType, Key(command.ProductId),
                before: null, after: new { MediaId = mediaId, ObjectKey = objectKey, Rights = command.Rights.ToString() });
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception exception)
        {
            // The object went up first so a saved row can never point at nothing. The cost is
            // that a failed save leaves an orphan, which is cleaned up here — best effort,
            // because the upload already failed and a second failure must not hide the first.
            await ForgetObjectAsync(objectKey, ct);

            if (exception is DomainException domainException)
                return Result.Failure<long>(Domain(domainException));

            throw;
        }

        // An image changes how a product looks, not whether it can be bought — except that a
        // usable image is what lets it go active at all, and that transition is its own call.
        await notifier.DisplayChangedAsync(ct);
        return Result.Success(mediaId);
    }

    public async Task<Result> RemoveMediaAsync(long productId, long mediaId, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var product = await products.GetByIdAsync(productId, ct);
        if (product is null) return Result.Failure(ProductNotFound());

        ProductMedia removed;
        try
        {
            removed = product.RemoveMedia(mediaId, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(Domain(exception));
        }

        audit.Record(actor.ToAudit(), "product.media_removed", TargetType, Key(productId),
            new { MediaId = mediaId, removed.ObjectKey }, after: null);
        await unitOfWork.SaveChangesAsync(ct);

        // Saved first, deleted after: the reverse would leave a row pointing at a missing object
        // if the save then failed, and a broken image is worse than an unreferenced one.
        await ForgetObjectAsync(removed.ObjectKey, ct);

        await notifier.DisplayChangedAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Reads enough of the stream to identify it and hands back a stream that still starts at
    /// byte zero, since the caller has to upload the whole thing afterwards.
    /// <para>
    /// A rewind is preferred where the stream allows one: an ASP.NET form file is buffered and
    /// therefore seekable, and a plain seekable stream is what the storage client needs to know
    /// the content length. The replay path remains for a stream that cannot be rewound.
    /// </para>
    /// </summary>
    private async Task<(FileTypeDetectionResult Detected, Stream Replayed)> SniffAsync(
        Stream content, CancellationToken ct)
    {
        var header = new byte[fileTypes.HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var detected = fileTypes.Detect(header.AsSpan(0, read));

        if (content.CanSeek)
        {
            content.Seek(0, SeekOrigin.Begin);
            return (detected, content);
        }

        return (detected, new ConcatenatedStream(header.AsMemory(0, read), content));
    }

    private async Task ForgetObjectAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await media.DeleteAsync(objectKey, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Could not delete the catalogue image {ObjectKey}; it is now unreferenced.", objectKey);
        }
    }

    public async Task<Result<BulkEditResult>> BulkEditAsync(BulkProductEdit edit, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(actor);

        if (edit.ProductIds is null || edit.ProductIds.Count == 0 || edit.ProductIds.Count > MaxBulkProducts)
        {
            return Result.Failure<BulkEditResult>(new Error(
                "VALIDATION_FAILED",
                $"A bulk edit covers between 1 and {MaxBulkProducts.ToString(CultureInfo.InvariantCulture)} products."));
        }

        if (edit.Status == BulkStatusChange.None && edit.PrimaryCategoryId is null && edit.BrandId is null)
        {
            return Result.Failure<BulkEditResult>(new Error(
                "VALIDATION_FAILED", "A bulk edit must change the status, the category or the brand."));
        }

        if (edit.BrandId is { } brandId && !await brands.ExistsAsync(brandId, ct))
            return Result.Failure<BulkEditResult>(BrandNotFound(brandId));

        if (edit.PrimaryCategoryId is { } categoryId &&
            await categories.GetByIdAsync(categoryId, ct) is null)
            return Result.Failure<BulkEditResult>(CategoryNotFound(categoryId));

        var ordered = edit.ProductIds.Distinct().ToArray();
        var loaded = (await products.GetByIdsAsync(ordered, ct)).ToDictionary(product => product.Id);
        var outcomes = new List<BulkItemOutcome>(ordered.Length);
        var sellabilityChanged = false;

        foreach (var productId in ordered)
        {
            if (!loaded.TryGetValue(productId, out var product))
            {
                outcomes.Add(new BulkItemOutcome(productId, false, "PRODUCT_NOT_FOUND"));
                continue;
            }

            var before = Snapshot(product);
            try
            {
                // Checked before anything is written, so a product that cannot go active is left
                // exactly as it was rather than half-edited into the new category.
                if (edit.Status == BulkStatusChange.Activate && product.ActivationProblems() is { Count: > 0 } problems)
                    throw new DomainException("PRODUCT_ACTIVATION_INVALID", string.Join(',', problems));

                if (edit.BrandId is { } newBrand || edit.PrimaryCategoryId is { } newCategory)
                {
                    product.UpdateDetails(
                        edit.BrandId ?? product.BrandId,
                        edit.PrimaryCategoryId ?? product.PrimaryCategoryId,
                        product.Title, product.ShortDescription, product.Description, clock.UtcNow);

                    sellabilityChanged |= product.Status != ProductStatus.Draft;
                }

                switch (edit.Status)
                {
                    case BulkStatusChange.Activate:
                        product.Activate(byAdmin: true, clock.UtcNow);
                        sellabilityChanged = true;
                        break;
                    case BulkStatusChange.Deactivate:
                        product.Deactivate(byAdmin: true, clock.UtcNow);
                        sellabilityChanged = true;
                        break;
                    default:
                        break;
                }
            }
            catch (DomainException exception)
            {
                outcomes.Add(new BulkItemOutcome(productId, false, exception.Code));
                continue;
            }

            audit.Record(actor.ToAudit(), "product.bulk_edited", TargetType, Key(productId),
                before, Snapshot(product));
            outcomes.Add(new BulkItemOutcome(productId, true, null));
        }

        // One save for the whole batch. These aggregates are independent — no product's
        // invariants depend on another's — so this is the batch rule 7 permits, not a
        // multi-aggregate transaction.
        await unitOfWork.SaveChangesAsync(ct);

        if (sellabilityChanged) await notifier.SellabilityChangedAsync(ct);
        else await notifier.DisplayChangedAsync(ct);

        return Result.Success(new BulkEditResult(outcomes));
    }

    public async Task<Result> MergeDuplicateAsync(long duplicateProductId, long survivorProductId,
        AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var duplicate = await products.GetByIdAsync(duplicateProductId, ct);
        if (duplicate is null) return Result.Failure(ProductNotFound());

        var survivor = await products.GetByIdAsync(survivorProductId, ct);
        if (survivor is null) return Result.Failure(ProductNotFound());

        // A survivor that is itself merged would leave a chain for every reader to walk, and the
        // next merge could close it into a loop.
        if (survivor.MergedIntoProductId.HasValue)
        {
            return Result.Failure(new Error(
                "PRODUCT_MERGE_INVALID",
                "The surviving product was itself merged into another; merge into that one instead.",
                409));
        }

        var before = Snapshot(duplicate);
        try
        {
            // Only the duplicate is written. Its identifiers stay where they are, so every
            // uniqueness index keeps holding, and the publishing port follows
            // MergedIntoProductId to the survivor instead. Moving them would mean two aggregates
            // in one save whose invariants depend on each other — exactly what rule 7 forbids.
            duplicate.MarkMergedInto(survivorProductId, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(Domain(exception));
        }

        audit.Record(actor.ToAudit(), "product.merged", TargetType, Key(duplicateProductId),
            before, new { MergedInto = survivorProductId, Status = duplicate.Status.ToString() });
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
