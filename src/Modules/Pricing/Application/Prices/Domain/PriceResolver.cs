using EwaysVNext.Modules.Pricing.Application.Bands;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application.Prices.Domain;

/// <summary>
/// Turns one variant's stored price into a quote in the currency a customer is shopping in.
/// <para>
/// Pure, and takes the rate lookup as a function rather than a port: resolving a hundred
/// variants reads one book of rates and then asks it a hundred times, so the cost of the lookup
/// is the caller's to arrange and the arithmetic here stays testable without one.
/// </para>
/// </summary>
public static class PriceResolver
{
    /// <summary>
    /// The quote, or null with <paramref name="error"/> set.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception because the caller is usually quoting a page of variants
    /// and one being unquotable is ordinary: it is dropped from the list, not propagated as a
    /// failure of the whole page.
    /// </remarks>
    public static PriceQuote? Resolve(
        VariantPrice price,
        string currency,
        Func<string, string, Result<FxResolution>> resolveRate,
        out Error? error)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(resolveRate);

        error = null;
        var normalizedCurrency = Currencies.Domain.Currency.NormalizeCode(currency);

        // An admin's price for this currency, set because the converted one was honest but
        // unsellable. Nothing is looked up and no rate is recorded: there was no conversion.
        if (price.FindOverride(normalizedCurrency) is { } @override)
        {
            return Quote(price, normalizedCurrency, @override.AmountMinor, PriceSource.Override, null);
        }

        if (string.Equals(price.BaseCurrency, normalizedCurrency, StringComparison.Ordinal))
        {
            return Quote(price, normalizedCurrency, price.BaseAmountMinor, PriceSource.Base, null);
        }

        var rate = resolveRate(price.BaseCurrency, normalizedCurrency);
        if (rate.IsFailure)
        {
            error = rate.Error;
            return null;
        }

        // Half-even to whole minor units: this is the last rounding before an amount becomes a
        // price someone pays, and half-even keeps a catalogue of thousands of conversions from
        // drifting upward the way half-up would.
        var converted = decimal.Round(
            price.BaseAmountMinor * rate.Value!.Rate, 0, MidpointRounding.ToEven);

        if (converted < 1m)
        {
            // Below one minor unit there is no price to show. Reporting it as missing rather
            // than rounding to zero keeps a free item out of the catalogue.
            error = PricingErrors.PriceMissing;
            return null;
        }

        return Quote(price, normalizedCurrency, checked((long)converted), PriceSource.Fx, rate.Value.Snapshot);
    }

    private static PriceQuote Quote(
        VariantPrice price, string currency, long referenceMinor, PriceSource source, FxSnapshot? fx)
    {
        var (lo, hi) = PriceBand.For(referenceMinor);
        return new PriceQuote(price.VariantId, currency, referenceMinor, lo, hi, source, fx);
    }
}
