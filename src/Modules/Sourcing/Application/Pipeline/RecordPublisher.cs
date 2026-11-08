using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourceListings;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Normalization;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sourcing.Application.Pipeline;

/// <summary>
/// Turns a validated record into a real product at a real price.
/// </summary>
/// <remarks>
/// <para>
/// The order is deliberate and the failures are handled differently at each step. The catalogue
/// goes first, because a price with no variant to attach it to is meaningless. If the catalogue
/// refuses, nothing else is attempted and the record goes back to review carrying the reason.
/// </para>
/// <para>
/// <b>A price failure does not undo the product.</b> Catalog has already committed — there is no
/// shared transaction, by design — so the product exists whatever happens next. The record is
/// marked published and the price problem is recorded as a review item, because telling an
/// administrator "the import failed" when a product was in fact created is the one answer that
/// would send them looking in the wrong place.
/// </para>
/// </remarks>
public sealed class RecordPublisher(
    ICatalogPublisher catalog,
    IPricePublisher prices,
    ISourceListingRepository listings,
    IIdGenerator ids,
    IClock clock)
{
    public async Task<Result<PublishOutcome>> PublishAsync(
        StagedRecord record,
        ProcessedRecord processed,
        IReadOnlyList<string> categoryKeyPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(processed);

        var product = processed.Product;
        var now = clock.UtcNow;

        var listing = await EnsureListingAsync(record, processed, now, ct);

        var published = await catalog.PublishAsync(
            new SourcingPublishRequest(
                // Validation has already refused a record with no title, and a missing brand is
                // a review item that must have been settled before this point.
                product.Brand ?? "Unbranded",
                categoryKeyPath,
                product.Name!,
                product.Description,
                product.Gtin,
                product.Mpn,
                product.Sku,
                [.. record.ImageKeys.Select((key, index) =>
                    new SourcingPublishMedia(key, product.Name, record.SourceUrl, index))],
                [new SourcingPublishVariant(product.Attributes, product.Sku)],
                [listing.Id],
                $"import:{record.LastJobId}",
                ImagesAllowed: processed.Rights.Status != RightsStatus.ImagesDenied,
                processed.Match.ProductId),
            ct);

        if (published.IsFailure) return Result.Failure<PublishOutcome>(published.Error!);

        var result = published.Value!;
        var warnings = new List<string>();

        // Everything below here happens after Catalog has committed.
        if (!result.SkippedBecauseDeactivated
            && product.PriceMinor is { } amount
            && product.Currency is { } currency)
        {
            foreach (var variant in result.Variants)
            {
                var priced = await prices.SetPriceAsync(
                    variant.VariantId, amount, currency, record.SourceUrl, now, ct);

                if (priced.IsFailure)
                {
                    warnings.Add(priced.Error!.Code);
                    break;
                }
            }

            listing.Observe(
                ids.Generate(), product.VariantKey(), amount, currency, null, now);
        }

        return Result.Success(new PublishOutcome(
            result.ProductId,
            [.. result.Variants.Select(variant => variant.VariantId)],
            result.SkippedBecauseDeactivated,
            listing.Id,
            warnings));
    }

    /// <summary>
    /// The source listing this record came from, created once and refreshed thereafter.
    /// </summary>
    /// <remarks>
    /// Keyed by URL, so a re-import updates the listing rather than adding another: the price
    /// history belongs to the page, not to the run that happened to read it.
    /// </remarks>
    private async Task<SourceListing> EnsureListingAsync(
        StagedRecord record, ProcessedRecord processed, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await listings.FindByUrlAsync(record.SourceUrl, ct);

        if (existing is not null)
        {
            existing.Refresh(
                processed.Product.Name ?? existing.Title,
                processed.Product.ImageUrls,
                processed.Rights.UsageRightsLabel,
                now);

            return existing;
        }

        var listing = SourceListing.Create(
            ids.Generate(),
            record.SourceUrl,
            record.SourceDomain ?? "file",
            null,
            processed.Product.Name ?? record.SourceUrl,
            processed.Product.ImageUrls,
            processed.Rights.UsageRightsLabel,
            record.Id,
            now);

        await listings.AddAsync(listing, ct);

        return listing;
    }
}

/// <remarks>
/// <c>Warnings</c> carries what went wrong after the product was created — a price that could
/// not be set, most often. The product exists; somebody needs to know the rest did not finish.
/// </remarks>
public sealed record PublishOutcome(
    long ProductId,
    IReadOnlyList<long> VariantIds,
    bool SkippedBecauseDeactivated,
    long SourceListingId,
    IReadOnlyList<string> Warnings);
