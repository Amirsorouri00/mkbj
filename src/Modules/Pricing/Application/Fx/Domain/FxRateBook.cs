using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Domain;

/// <summary>
/// Everything needed to resolve a rate, read once and passed in whole.
/// </summary>
/// <remarks>
/// A snapshot rather than a set of repositories, so resolution is a pure function. A quote that
/// reached the database mid-calculation could use a rate from before a refresh for one leg and
/// after it for another, and the cross rate would then be of a moment that never existed.
/// </remarks>
/// <param name="LatestProviderRates">The newest provider row per pair.</param>
/// <param name="ActiveOverrides">Overrides that have not been withdrawn; expiry is checked here.</param>
/// <param name="MaxRateAge">How old a provider rate may be and still be quoted from.</param>
public sealed record FxRateBook(
    IReadOnlyList<FxRate> LatestProviderRates,
    IReadOnlyList<FxRateOverride> ActiveOverrides,
    TimeSpan MaxRateAge);

/// <remarks>
/// <c>ValidUntil</c> is when this answer stops being usable, so a caller holding a quote knows
/// whether to re-ask. Null means it does not expire on its own — a same-currency rate, the AED
/// peg, or an override an admin has not time-limited.
/// </remarks>
public sealed record FxResolution(
    decimal Rate,
    PriceSource Source,
    FxSnapshot? Snapshot,
    DateTimeOffset? ValidUntil);
