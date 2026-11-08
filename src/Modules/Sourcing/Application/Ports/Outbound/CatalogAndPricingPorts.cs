using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;

/// <summary>One candidate the catalogue offered for a listing.</summary>
public sealed record CatalogCandidate(long ProductId, string Title, double Similarity);

/// <summary>
/// Asking the catalogue whether it already has this.
/// </summary>
/// <remarks>
/// Deliberately two questions, not one. An identifier match is a fact — this barcode belongs to
/// that product — while a title match is a suggestion, and the pipeline treats them completely
/// differently: one publishes, the other asks a person.
/// </remarks>
public interface ICatalogMatcher
{
    /// <summary>The product carrying this exact identifier, if the catalogue has one.</summary>
    Task<long?> FindByIdentifierAsync(
        string identifierType, string value, CancellationToken ct = default);

    /// <summary>
    /// Products whose titles look like this one, best first.
    /// </summary>
    /// <remarks>
    /// Trigram similarity in the database rather than in this process: the catalogue has
    /// hundreds of thousands of titles, and pulling them all out to compare would be slower than
    /// the import it is meant to speed up.
    /// </remarks>
    Task<IReadOnlyList<CatalogCandidate>> FindSimilarAsync(
        string title, double threshold, int limit, CancellationToken ct = default);
}

public sealed record SourcingPublishMedia(
    string StagingObjectKey, string? Alt, string? SourceUrl, int SortOrder);

public sealed record SourcingPublishVariant(
    IReadOnlyDictionary<string, string> Attributes, string? Sku);

/// <summary>
/// What Sourcing asks Catalog to make true.
/// </summary>
/// <remarks>
/// A local shape rather than Catalog's own command: the gateway maps between them, so a change
/// to Catalog's publishing contract is a compile error in one file instead of a change to the
/// pipeline.
/// </remarks>
public sealed record SourcingPublishRequest(
    string BrandName,
    IReadOnlyList<string> CategoryKeyPath,
    string Title,
    string? Description,
    string? Gtin,
    string? Mpn,
    string? Sku,
    IReadOnlyList<SourcingPublishMedia> Media,
    IReadOnlyList<SourcingPublishVariant> Variants,
    IReadOnlyList<long> SourceListingIds,
    string Provenance,
    bool ImagesAllowed,
    long? TargetProductId);

public sealed record SourcingPublishedVariant(long VariantId, string Sku);

/// <remarks>
/// <c>SkippedBecauseDeactivated</c> travels all the way back to the staged record, because the
/// review queue has to be able to say why a published import changed nothing.
/// </remarks>
public sealed record SourcingPublishResult(
    long ProductId,
    bool Created,
    bool SkippedBecauseDeactivated,
    IReadOnlyList<SourcingPublishedVariant> Variants);

public interface ICatalogPublisher
{
    Task<Result<SourcingPublishResult>> PublishAsync(
        SourcingPublishRequest request, CancellationToken ct = default);
}

public interface IPricePublisher
{
    Task<Result> SetPriceAsync(
        long variantId,
        long amountMinor,
        string currency,
        string sourceUrl,
        DateTimeOffset collectedAt,
        CancellationToken ct = default);
}
