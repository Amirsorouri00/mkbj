using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Gateways;

/// <summary>
/// Publishes a validated record into the catalogue.
/// </summary>
/// <remarks>
/// <para>
/// The mapping lives here so that a change to Catalog's publishing contract is a compile error
/// in one file rather than a change to the pipeline. None of this shares a transaction with the
/// staged record it came from: Catalog commits its own write, and the staged record is marked
/// published afterwards.
/// </para>
/// <para>
/// <b>Image rights are enforced on the way out.</b> A record whose source denied image use
/// arrives here with its pictures still attached — they were downloaded before anybody asked —
/// and this is the last point at which they can be left behind.
/// </para>
/// </remarks>
internal sealed class CatalogPublishingGateway(ICatalogPublishingPort catalog) : ICatalogPublisher
{
    public async Task<Result<SourcingPublishResult>> PublishAsync(
        SourcingPublishRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identifiers = new List<IdentifierInput>();

        if (request.Gtin is { } gtin)
            identifiers.Add(new IdentifierInput(ProductIdentifierType.Gtin, gtin, null));

        if (request.Mpn is { } mpn)
            identifiers.Add(new IdentifierInput(ProductIdentifierType.Mpn, mpn, null));

        var media = request.ImagesAllowed
            ? request.Media.Select(image => new PublishMedia(
                image.StagingObjectKey,
                image.Alt,
                image.SourceUrl,
                MediaUsageRights.Allowed,
                image.SortOrder)).ToList()
            : [];

        var published = await catalog.UpsertProductAsync(
            new PublishProductCommand(
                new PublishBrand(request.BrandName, []),
                request.CategoryKeyPath,
                request.Title,
                ShortDescription: null,
                request.Description,
                identifiers,
                media,
                [.. request.Variants.Select(variant =>
                    new PublishVariant(variant.Attributes, variant.Sku))],
                request.SourceListingIds,
                request.Provenance,
                request.TargetProductId),
            ct);

        if (published.IsFailure) return Result.Failure<SourcingPublishResult>(published.Error!);

        var value = published.Value!;

        return Result.Success(new SourcingPublishResult(
            value.ProductId,
            value.Created,
            value.SkippedBecauseDeactivated,
            [.. value.Variants.Select(variant =>
                new SourcingPublishedVariant(variant.VariantId, variant.Sku))]));
    }
}

/// <summary>
/// Sets an imported price, citing where it was seen.
/// </summary>
/// <remarks>
/// The provenance is the point. An imported price that cannot be traced to a page and a moment
/// is one nobody can check against the supplier when it turns out to be wrong.
/// </remarks>
internal sealed class PricePublishingGateway(IPricePublishingPort prices) : IPricePublisher
{
    public Task<Result> SetPriceAsync(
        long variantId,
        long amountMinor,
        string currency,
        string sourceUrl,
        DateTimeOffset collectedAt,
        CancellationToken ct = default) =>
        prices.SetReferencePriceAsync(
            variantId,
            amountMinor,
            currency,
            new PriceProvenance(
                ObservationId: null,
                sourceUrl,
                collectedAt,
                OriginalAmount: null,
                OriginalCurrency: null,
                TaxIncluded: null),
            ct);
}
