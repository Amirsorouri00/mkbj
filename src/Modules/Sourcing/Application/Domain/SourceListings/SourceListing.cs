using System.Text.RegularExpressions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;

namespace EwaysVNext.Modules.Sourcing.Application.Domain.SourceListings;

/// <summary>
/// One price, as seen at one source at one moment.
/// </summary>
/// <remarks>
/// Append-only. A price that changed is a new observation, never an edit of the old one — the
/// history is the point, and overwriting it would throw away the only evidence of what a
/// supplier was charging last month.
/// </remarks>
public sealed class PriceObservation : Entity<long>
{
    private PriceObservation() { }

    internal PriceObservation(
        long id,
        string variantKey,
        long amountMinor,
        string currency,
        bool? taxIncluded,
        DateTimeOffset collectedAt)
    {
        Id = id;
        VariantKey = variantKey;
        AmountMinor = amountMinor;
        Currency = currency;
        TaxIncluded = taxIncluded;
        CollectedAt = collectedAt;
    }

    /// <summary>Which variant of the listing this price was for.</summary>
    public string VariantKey { get; private set; } = string.Empty;

    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the source said its price includes tax.
    /// </summary>
    /// <remarks>
    /// Nullable because most sources do not say, and guessing would be worse than admitting it:
    /// a price recorded as tax-inclusive when it was not is wrong by the tax rate.
    /// </remarks>
    public bool? TaxIncluded { get; private set; }

    public DateTimeOffset CollectedAt { get; private set; }
}

/// <summary>
/// What one source had on one page, and what it has charged over time.
/// </summary>
/// <remarks>
/// Separate from the staged record because the two answer different questions. A staged record
/// is about <b>importing</b> — did this become a product? A source listing is about the
/// <b>source</b> — what has this supplier been charging, and may we use their material? The
/// listing outlives any particular import.
/// </remarks>
public sealed class SourceListing : AggregateRoot<long>
{
    private static readonly Regex CurrencyPattern = new("^[A-Z]{3}$", RegexOptions.Compiled);

    private readonly List<PriceObservation> _observations = [];

    private SourceListing() { }

    public string Url { get; private set; } = string.Empty;
    public string Domain { get; private set; } = string.Empty;
    public string? SourceId { get; private set; }
    public DateTimeOffset CollectedAt { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public IReadOnlyList<string> ImageUrls { get; private set; } = [];

    /// <summary>The rights label that was in force when this was collected.</summary>
    public string Rights { get; private set; } = string.Empty;

    public long StagedRecordId { get; private set; }

    public IReadOnlyCollection<PriceObservation> Observations => _observations;

    public static SourceListing Create(
        long id,
        string url,
        string domain,
        string? sourceId,
        string title,
        IReadOnlyList<string> imageUrls,
        string rights,
        long stagedRecordId,
        DateTimeOffset collectedAt) =>
        new()
        {
            Id = id,
            Url = url,
            Domain = SourceDomain.Normalize(domain),
            SourceId = sourceId,
            Title = title,
            ImageUrls = [.. imageUrls],
            Rights = rights,
            StagedRecordId = stagedRecordId,
            CollectedAt = collectedAt,
        };

    public void Refresh(
        string title, IReadOnlyList<string> imageUrls, string rights, DateTimeOffset collectedAt)
    {
        Title = title;
        ImageUrls = [.. imageUrls];
        Rights = rights;
        CollectedAt = collectedAt;
    }

    /// <summary>
    /// Records a price, or returns the one already recorded for that variant at that moment.
    /// </summary>
    /// <remarks>
    /// Idempotent per variant and instant, because a step that is redelivered must not turn one
    /// observation into two and make a flat price look like a flurry of changes.
    /// </remarks>
    public PriceObservation Observe(
        long observationId,
        string variantKey,
        long amountMinor,
        string currency,
        bool? taxIncluded,
        DateTimeOffset collectedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variantKey);

        if (amountMinor <= 0)
        {
            throw new DomainException(
                "PRICE_OBSERVATION_AMOUNT_INVALID", "An observed price must be above zero.");
        }

        if (currency is null || !CurrencyPattern.IsMatch(currency))
        {
            throw new DomainException(
                "PRICE_OBSERVATION_CURRENCY_INVALID",
                "A currency is three upper-case letters.");
        }

        var existing = _observations.FirstOrDefault(observation =>
            string.Equals(observation.VariantKey, variantKey, StringComparison.Ordinal)
            && observation.CollectedAt == collectedAt);

        if (existing is not null) return existing;

        var observation = new PriceObservation(
            observationId, variantKey, amountMinor, currency, taxIncluded, collectedAt);

        _observations.Add(observation);

        return observation;
    }
}
