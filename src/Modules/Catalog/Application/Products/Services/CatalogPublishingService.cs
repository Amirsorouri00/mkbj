using System.Globalization;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Catalog.Application.Products.Services;

public sealed class CatalogPublishingService(
    IProductRepository products,
    IBrandAdminService brands,
    ICategoryRepository categories,
    ICatalogMediaStore media,
    ICatalogChangeNotifier notifier,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : ICatalogPublishingPort
{
    /// <summary>
    /// How far a merge chain is followed before it is treated as broken. A legitimate chain is
    /// one or two hops; ten means something wrote a loop past the aggregate, and walking it
    /// forever is worse than refusing.
    /// </summary>
    private const int MaxMergeHops = 10;

    public async Task<Result<PublishedProduct>> UpsertProductAsync(
        PublishProductCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The brand first, and in its own save. It is the one thing two import workers race for,
        // and EnsureAsync is built to lose that race gracefully.
        var brand = await brands.EnsureAsync(
            command.Brand.Name, command.Brand.Aliases ?? [], command.Provenance, ct);
        if (brand.IsFailure) return Result.Failure<PublishedProduct>(brand.Error!);

        var category = await categories.FindByCanonicalPathAsync(command.CategoryKeyPath ?? [], ct);
        if (category is null)
        {
            // Nothing is written. A product filed under a guess is worse than one not imported:
            // the guess is invisible, and the missing import is in the job's report.
            return Result.Failure<PublishedProduct>(new Error(
                "PUBLISH_CATEGORY_UNRESOLVED",
                $"No category answers to '{string.Join(" / ", command.CategoryKeyPath ?? [])}'.",
                422));
        }

        var match = await FindTargetAsync(command, brand.Value, ct);
        if (match.IsFailure) return Result.Failure<PublishedProduct>(match.Error!);

        var product = match.Value;
        var created = product is null;

        try
        {
            product = created
                ? NewDraft(command, brand.Value, category.Id)
                : Refresh(product!, command, brand.Value, category.Id);

            ApplyIdentifiers(product, command);
            var variants = ApplyVariants(product, command);
            await ApplyMediaAsync(product, command, ct);
            ApplySourceRefs(product, command);

            // byAdmin: false — an import may make a product live, but may never undo an admin
            // who switched it off.
            var activated = product.Activate(byAdmin: false, clock.UtcNow);
            var skipped = !activated && product.DeactivatedByAdmin;

            if (created)
            {
                await products.AddAsync(product, ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
            await notifier.SellabilityChangedAsync(ct);

            return Result.Success(new PublishedProduct(product.Id, created, skipped, variants));
        }
        catch (DomainException exception)
        {
            // Nothing has been saved at this point — a new product was never added, and an
            // existing one is a tracked instance the caller's scope will discard unsaved.
            return Result.Failure<PublishedProduct>(new Error(exception.Code, exception.Message));
        }
    }

    /// <summary>
    /// The product this listing is about, or null to create one.
    /// <para>
    /// Matching runs in order of how much each identifier proves: a GS1 code names the article
    /// worldwide, a brand and part number name it within one manufacturer, and a source id names
    /// it only inside the system it came from. First hit wins, because a weaker match that
    /// disagrees with a stronger one is the weaker one being wrong.
    /// </para>
    /// </summary>
    private async Task<Result<Product?>> FindTargetAsync(
        PublishProductCommand command, long brandId, CancellationToken ct)
    {
        if (command.TargetProductId is { } targetId)
        {
            var target = await products.GetByIdAsync(targetId, ct);
            return target is null
                ? Result.Failure<Product?>(new Error("PRODUCT_NOT_FOUND", "Product was not found.", 404))
                : await FollowMergesAsync(target, ct);
        }

        foreach (var identifier in command.Identifiers ?? [])
        {
            if (identifier.Type is not (ProductIdentifierType.Gtin or ProductIdentifierType.Upc
                or ProductIdentifierType.Ean))
                continue;

            var value = Normalize(identifier);
            if (value is null) continue;

            if (await products.FindByIdentifierAsync(identifier.Type, value, null, ct) is { } byGs1)
                return await FollowMergesAsync(byGs1, ct);
        }

        foreach (var identifier in (command.Identifiers ?? []).Where(i => i.Type == ProductIdentifierType.Mpn))
        {
            var value = Normalize(identifier);
            if (value is null) continue;

            if (await products.FindByBrandAndMpnAsync(brandId, value, ct) is { } byMpn)
                return await FollowMergesAsync(byMpn, ct);
        }

        foreach (var identifier in (command.Identifiers ?? []).Where(i => i.Type == ProductIdentifierType.SourceId))
        {
            var value = Normalize(identifier);
            if (value is null) continue;

            if (await products.FindByIdentifierAsync(
                    identifier.Type, value, identifier.SourceDomain?.Trim().ToLowerInvariant(), ct) is { } bySource)
                return await FollowMergesAsync(bySource, ct);
        }

        return Result.Success<Product?>(null);
    }

    /// <summary>
    /// Walks a matched product to the one that survived its merges, so a feed that still carries
    /// a duplicate's code updates the product customers can actually buy.
    /// </summary>
    private async Task<Result<Product?>> FollowMergesAsync(Product product, CancellationToken ct)
    {
        var current = product;

        for (var hop = 0; current.MergedIntoProductId is { } survivorId; hop++)
        {
            if (hop >= MaxMergeHops)
            {
                return Result.Failure<Product?>(new Error(
                    "PRODUCT_MERGE_CYCLE",
                    "This product's merge chain does not end; it has to be untangled by hand.",
                    409));
            }

            var survivor = await products.GetByIdAsync(survivorId, ct);
            if (survivor is null)
            {
                return Result.Failure<Product?>(new Error(
                    "PRODUCT_NOT_FOUND", "The surviving product was not found.", 404));
            }

            current = survivor;
        }

        return Result.Success<Product?>(current);
    }

    private Product NewDraft(PublishProductCommand command, long brandId, long categoryId) =>
        Product.CreateDraft(
            ids.Generate(), brandId, categoryId, command.Title, slug: null,
            command.ShortDescription, command.Description, clock.UtcNow);

    private Product Refresh(Product product, PublishProductCommand command, long brandId, long categoryId)
    {
        product.UpdateDetails(
            brandId, categoryId, command.Title, command.ShortDescription, command.Description, clock.UtcNow);
        return product;
    }

    private void ApplyIdentifiers(Product product, PublishProductCommand command)
    {
        foreach (var identifier in command.Identifiers ?? [])
        {
            // AddIdentifier is itself a no-op for one already held, so re-publishing the same
            // feed row adds nothing.
            product.AddIdentifier(
                ids.Generate(), identifier.Type, identifier.Value, identifier.SourceDomain, clock.UtcNow);
        }
    }

    private List<PublishedVariant> ApplyVariants(Product product, PublishProductCommand command)
    {
        var published = new List<PublishedVariant>();

        foreach (var variant in command.Variants ?? [])
        {
            var signature = AttributeSignature.From(variant.Attributes);
            var existing = product.Variants.FirstOrDefault(candidate =>
                string.Equals(candidate.AttributeSignatureHash, signature.Hash, StringComparison.Ordinal));

            if (existing is null)
            {
                var variantId = ids.Generate();
                var sku = string.IsNullOrWhiteSpace(variant.Sku)
                    ? $"SKU-{variantId.ToString(CultureInfo.InvariantCulture)}"
                    : variant.Sku;

                var added = product.AddVariant(variantId, sku, variant.Attributes, clock.UtcNow);
                published.Add(new PublishedVariant(added.Id, added.Sku.Value, added.AttributeSignature, true));
                continue;
            }

            // Listed again by the source, so it is back in stock — unless an admin turned it off,
            // which SetVariantActive refuses to undo and reports by returning false.
            product.SetVariantActive(existing.Id, true, byAdmin: false, clock.UtcNow);
            published.Add(new PublishedVariant(
                existing.Id, existing.Sku.Value, existing.AttributeSignature, false));
        }

        return published;
    }

    private async Task ApplyMediaAsync(Product product, PublishProductCommand command, CancellationToken ct)
    {
        foreach (var image in command.Media ?? [])
        {
            if (string.IsNullOrWhiteSpace(image.StagingObjectKey)) continue;

            if (AlreadyPublished(product, image)) continue;

            var mediaId = ids.Generate();
            var objectKey =
                $"products/{product.Id.ToString(CultureInfo.InvariantCulture)}/{mediaId.ToString(CultureInfo.InvariantCulture)}{ExtensionOf(image.StagingObjectKey)}";

            // Copied server-side from staging. The bytes are already in object storage; pulling
            // them through this process to push them back would double the traffic for nothing.
            await media.CopyFromStagingAsync(image.StagingObjectKey, objectKey, ct);

            product.AddMedia(
                mediaId, objectKey, image.SortOrder, image.Alt, image.SourceUrl, image.Rights, clock.UtcNow);
        }
    }

    /// <summary>
    /// Whether this image is already on the product.
    /// <para>
    /// Matched on <c>SourceUrl</c> — where it came from — rather than on its bytes, because the
    /// same picture re-downloaded lands under a different staging key, and comparing content
    /// would mean fetching every image on every pass.
    /// </para>
    /// <para>
    /// A feed that supplies no source URL cannot be matched at all, so the gallery position is
    /// used instead. That is weaker: it will treat a genuinely new image inserted at an existing
    /// position as already published. The alternative — adding it anyway — grows the gallery by
    /// its whole length on every import run, which is the worse failure.
    /// </para>
    /// </summary>
    private static bool AlreadyPublished(Product product, PublishMedia image) =>
        image.SourceUrl is { Length: > 0 }
            ? product.Media.Any(existing =>
                string.Equals(existing.SourceUrl, image.SourceUrl, StringComparison.Ordinal))
            : product.Media.Any(existing => existing.SortOrder == image.SortOrder);

    private void ApplySourceRefs(Product product, PublishProductCommand command)
    {
        foreach (var sourceListingId in command.SourceListingIds ?? [])
        {
            if (sourceListingId > 0) product.AddSourceRef(sourceListingId, clock.UtcNow);
        }
    }

    /// <summary>
    /// The identifier's stored form, or null when it is unusable. A bad code in a feed row is
    /// not worth failing the whole import over — it just matches nothing.
    /// </summary>
    private static string? Normalize(IdentifierInput identifier)
    {
        try
        {
            return ProductIdentifier.Normalize(identifier.Type, identifier.Value, identifier.SourceDomain);
        }
        catch (DomainException)
        {
            return null;
        }
    }

    private static string ExtensionOf(string stagingObjectKey)
    {
        var dot = stagingObjectKey.LastIndexOf('.');
        return dot >= 0 ? stagingObjectKey[dot..] : string.Empty;
    }
}
