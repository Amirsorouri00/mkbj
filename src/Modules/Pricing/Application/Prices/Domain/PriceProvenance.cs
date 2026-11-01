namespace EwaysVNext.Modules.Pricing.Application.Prices.Domain;

/// <summary>
/// Where a base price came from, kept beside it.
/// </summary>
/// <remarks>
/// <para>
/// Every field here answers a question someone asks months later: which crawl produced this, what
/// page it was on, when it was read, and what the page actually said before any conversion. A
/// price with no provenance is a number nobody can defend.
/// </para>
/// <para>
/// <c>OriginalAmount</c> is a <c>decimal</c>, not minor units: it is what the source displayed,
/// in the source's own currency, and converting it on the way in would lose the thing being
/// recorded. <c>TaxIncluded</c> is nullable because many sources simply do not say.
/// </para>
/// </remarks>
public sealed record PriceProvenance(
    long? ObservationId,
    string? SourceUrl,
    DateTimeOffset? CollectedAt,
    decimal? OriginalAmount,
    string? OriginalCurrency,
    bool? TaxIncluded)
{
    /// <summary>An admin typing a price directly: no observation, no page, nothing to cite.</summary>
    public static readonly PriceProvenance Manual = new(null, null, null, null, null, null);
}
