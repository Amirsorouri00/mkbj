namespace EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;

/// <remarks><c>Rate</c> is what one euro buys in <c>Quote</c>.</remarks>
public sealed record FxQuote(string Quote, decimal Rate);

/// <summary>
/// One provider's published rates for one day.
/// </summary>
/// <remarks>
/// The base is always the euro and is therefore not a field: every provider the MVP uses
/// publishes against it, and the resolver crosses through it. A batch that could name its own
/// base would invite a second hub and two ways to compute the same cross rate.
/// </remarks>
public sealed record FxProviderBatch(string Provider, DateOnly RateDate, IReadOnlyList<FxQuote> Rates);
