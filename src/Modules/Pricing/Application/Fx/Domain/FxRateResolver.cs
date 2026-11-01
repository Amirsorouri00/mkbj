using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Domain;

/// <summary>
/// Turns a book of rates into the one rate a quote should use.
/// <para>
/// Pure: no IO, no clock of its own, <c>decimal</c> throughout. This runs inside composition,
/// which searches for a basket totalling an exact amount — so it has to give the same answer for
/// the same inputs every time, and a rate that moved mid-search would make the total it found
/// wrong by the time it was written down.
/// </para>
/// </summary>
public static class FxRateResolver
{
    private const string Ecb = "ecb";
    private const string EcbCross = "ecb:cross-eur";
    private const string EcbCrossPeg = "ecb:cross-eur+derived:usd-peg";

    /// <summary>Every provider rate is published against the euro, so it is the hub.</summary>
    private const string Hub = "EUR";

    private const string Usd = "USD";
    private const string Aed = "AED";

    /// <summary>
    /// Ten decimal places, half-even. The rate is multiplied by an amount before that amount is
    /// rounded to the cent, so rounding here is the only rounding the chain gets; half-even
    /// keeps a long run of conversions from drifting the way half-up would.
    /// </summary>
    private const int RateScale = 10;

    public static Result<FxResolution> Resolve(
        FxRateBook book, string @base, string quote, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(book);

        var from = Currency.NormalizeCode(@base);
        var to = Currency.NormalizeCode(quote);

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return Result.Success(new FxResolution(1m, PriceSource.Base, null, null));
        }

        // An admin's override comes first and is never checked for staleness: it is a decision,
        // not an observation, and the whole reason to set one is that the provider is wrong.
        if (EffectiveOverride(book, from, to, now) is { } @override)
        {
            return Result.Success(new FxResolution(
                @override.Rate,
                PriceSource.Fx,
                new FxSnapshot(null, @override.Id, from, to, @override.Rate, @override.SetAt, "override"),
                @override.ValidUntil));
        }

        // The peg, before any provider lookup: USD↔AED needs no published rate at all.
        if (DirectPeg(from, to) is { } pegged)
        {
            return Result.Success(new FxResolution(
                pegged,
                PriceSource.Fx,
                new FxSnapshot(null, null, from, to, pegged, now, UsdPeg.Provenance),
                null));
        }

        var fromLeg = Leg(book, from, now);
        if (fromLeg is null) return Result.Failure<FxResolution>(PricingErrors.FxRateUnavailable);

        var toLeg = Leg(book, to, now);
        if (toLeg is null) return Result.Failure<FxResolution>(PricingErrors.FxRateUnavailable);

        var rate = Round(toLeg.Value.Value / fromLeg.Value.Value);

        // The quote's leg identifies the conversion; when the quote is the hub itself there is no
        // such row, so the base's leg is what there is to point at.
        var sourceRow = toLeg.Value.Row ?? fromLeg.Value.Row;

        // The oldest leg, not the newest: a cross rate is only as current as its stalest input.
        var rateAt = Oldest(fromLeg.Value, toLeg.Value, now);

        return Result.Success(new FxResolution(
            rate,
            PriceSource.Fx,
            new FxSnapshot(sourceRow?.Id, null, from, to, rate, rateAt, Provenance(from, to, fromLeg.Value, toLeg.Value)),
            rateAt + book.MaxRateAge));
    }

    private static FxRateOverride? EffectiveOverride(
        FxRateBook book, string @base, string quote, DateTimeOffset now) =>
        book.ActiveOverrides?.FirstOrDefault(candidate =>
            string.Equals(candidate.Base, @base, StringComparison.Ordinal)
            && string.Equals(candidate.Quote, quote, StringComparison.Ordinal)
            && candidate.IsEffectiveAt(now));

    private static decimal? DirectPeg(string @base, string quote)
    {
        if (@base == Usd && quote == Aed) return UsdPeg.UsdToAed;
        if (@base == Aed && quote == Usd) return Round(1m / UsdPeg.UsdToAed);
        return null;
    }

    /// <summary>
    /// What one unit of the hub currency buys in <paramref name="currency"/>, and the row that
    /// says so.
    /// </summary>
    /// <remarks>
    /// The hub needs no row — one euro is one euro. The dirham has no published euro rate, so it
    /// is derived from the dollar leg times the peg, which is why asking for AED fails when the
    /// dollar rate is missing or stale.
    /// </remarks>
    private static (decimal Value, FxRate? Row, bool ViaPeg)? Leg(
        FxRateBook book, string currency, DateTimeOffset now)
    {
        if (currency == Hub) return (1m, null, false);

        if (currency == Aed)
        {
            var usd = FreshRow(book, Usd, now);
            return usd is null ? null : (usd.Rate * UsdPeg.UsdToAed, usd, true);
        }

        var row = FreshRow(book, currency, now);
        return row is null ? null : (row.Rate, row, false);
    }

    /// <summary>
    /// The newest hub rate for a currency, if it is young enough to quote from.
    /// <para>
    /// The age bound is <b>inclusive</b>: a rate exactly at the limit is still usable. The limit
    /// is a policy about how stale is tolerable, and refusing a rate that is precisely as old as
    /// the number an admin typed would make the setting mean something other than it says.
    /// </para>
    /// </summary>
    private static FxRate? FreshRow(FxRateBook book, string quote, DateTimeOffset now) =>
        book.LatestProviderRates?
            .Where(rate =>
                string.Equals(rate.Base, Hub, StringComparison.Ordinal)
                && string.Equals(rate.Quote, quote, StringComparison.Ordinal)
                && now - rate.FetchedAt <= book.MaxRateAge)
            .MaxBy(rate => rate.FetchedAt);

    private static DateTimeOffset Oldest(
        (decimal Value, FxRate? Row, bool ViaPeg) from,
        (decimal Value, FxRate? Row, bool ViaPeg) to,
        DateTimeOffset now)
    {
        var times = new[] { from.Row?.FetchedAt, to.Row?.FetchedAt }
            .Where(fetchedAt => fetchedAt.HasValue)
            .Select(fetchedAt => fetchedAt!.Value)
            .ToArray();

        // Both legs are the hub only when base equals quote, which returned long ago.
        return times.Length == 0 ? now : times.Min();
    }

    /// <summary>
    /// How the rate was arrived at, in a form an auditor can read. The peg dominates: if a
    /// dirham is anywhere in the chain, the answer is part derivation whatever else it used.
    /// </summary>
    private static string Provenance(
        string @base, string quote,
        (decimal Value, FxRate? Row, bool ViaPeg) from,
        (decimal Value, FxRate? Row, bool ViaPeg) to)
    {
        if (from.ViaPeg || to.ViaPeg) return EcbCrossPeg;

        // One of the two is the hub, so only one provider row was read.
        return @base == Hub || quote == Hub ? Ecb : EcbCross;
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, RateScale, MidpointRounding.ToEven);
}
