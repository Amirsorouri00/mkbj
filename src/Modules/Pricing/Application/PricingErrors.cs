using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application;

/// <summary>
/// Every refusal pricing can give, with the status it means.
/// <para>
/// Gathered in one place because several of these are the difference between "we cannot sell you
/// this" and "we cannot sell you this <em>right now</em>", and a caller deciding whether to retry
/// has to be able to tell them apart from the code alone.
/// </para>
/// </summary>
public static class PricingErrors
{
    /// <summary>The currency exists but the seller has not turned it on. Not a retry.</summary>
    public static readonly Error CurrencyNotEnabled = new(
        "CURRENCY_NOT_ENABLED", "This currency is not enabled for sale.", 422);

    /// <summary>
    /// No usable exchange rate — missing, or older than the configured limit. Retryable, because
    /// the refresh job will almost certainly fix it: pricing refuses rather than quoting from a
    /// stale rate, since a wrong price honoured is worse than a quote declined.
    /// </summary>
    public static readonly Error FxRateUnavailable = new(
        "FX_RATE_UNAVAILABLE", "No usable exchange rate is available for this currency.", 503, true);

    /// <summary>The variant is sellable but nobody has priced it. An omission, not a failure.</summary>
    public static readonly Error PriceMissing = new(
        "PRICE_MISSING", "This item has no price.", 422);

    public static readonly Error VariantNotFound = new(
        "VARIANT_NOT_FOUND", "The variant was not found.", 404);

    public static readonly Error CurrencyNotFound = new(
        "CURRENCY_NOT_FOUND", "The currency was not found.", 404);

    /// <summary>An override already covers this currency and period.</summary>
    public static readonly Error FxOverrideConflict = new(
        "FX_OVERRIDE_CONFLICT", "An exchange-rate override already covers this period.", 409);
}
