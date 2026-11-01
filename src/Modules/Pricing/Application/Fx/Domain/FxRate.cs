using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Domain;

/// <summary>
/// One exchange rate as one provider published it on one day.
/// <para>
/// Append-only: there are no mutators, and a correction is a new row rather than an edit. What a
/// price was quoted against has to stay knowable after the fact, and an overwritten rate makes
/// every invoice that used it unexplainable.
/// </para>
/// </summary>
public sealed class FxRate : AggregateRoot<long>
{
    public string Base { get; private set; } = null!;

    public string Quote { get; private set; } = null!;

    /// <summary>
    /// <c>numeric(18,10)</c>. Ten decimal places because a rate is multiplied by an amount before
    /// it is rounded, so precision lost here is money lost at the other end.
    /// </summary>
    public decimal Rate { get; private set; }

    public string Provider { get; private set; } = null!;

    /// <summary>The day the rate is <em>for</em>, which is not the day it was fetched.</summary>
    public DateOnly RateDate { get; private set; }

    public DateTimeOffset FetchedAt { get; private set; }

    private FxRate() { }

    public static FxRate Record(long id, string @base, string quote, decimal rate, string provider,
        DateOnly rateDate, DateTimeOffset fetchedAt)
    {
        if (id <= 0)
        {
            throw new DomainException("FX_RATE_ID_INVALID", "FX rate ID must be positive.");
        }

        var normalizedBase = Currency.NormalizeCode(@base);
        var normalizedQuote = Currency.NormalizeCode(quote);
        EnsureDifferentPair(normalizedBase, normalizedQuote);
        EnsurePositiveRate(rate);

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new DomainException("FX_PROVIDER_REQUIRED", "The rate's provider is required.");
        }

        return new FxRate
        {
            Id = id,
            Base = normalizedBase,
            Quote = normalizedQuote,
            Rate = rate,
            Provider = provider.Trim(),
            RateDate = rateDate,
            FetchedAt = fetchedAt,
        };
    }

    internal static void EnsureDifferentPair(string @base, string quote)
    {
        if (string.Equals(@base, quote, StringComparison.Ordinal))
        {
            throw new DomainException(
                "FX_PAIR_INVALID", $"A rate from {@base} to itself is not a rate.");
        }
    }

    internal static void EnsurePositiveRate(decimal rate)
    {
        if (rate <= 0)
        {
            throw new DomainException(
                "FX_RATE_INVALID", "An exchange rate must be greater than zero.");
        }
    }
}
