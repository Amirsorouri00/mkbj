using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;

/// <remarks>
/// <c>Fx</c> is null when no conversion happened — the amount was already in dollars. Otherwise
/// it is the rate that produced the figure, so it can be explained later.
/// </remarks>
public sealed record UsdConversion(long AmountMinor, FxSnapshot? Fx);

/// <summary>
/// Converts an amount to US dollars.
/// <para>
/// A port of its own because the dollar figure is not a price: it is what the seller's commerce
/// limits are expressed in, so an order in dirhams can be checked against a minimum written in
/// dollars. Quoting and limit-checking are different questions and answering both through one
/// method would mean one of them carrying arguments it does not use.
/// </para>
/// </summary>
public interface IFxSnapshotPort
{
    Task<Result<UsdConversion>> ToUsdAsync(
        long amountMinor, string currency, DateTimeOffset now, CancellationToken ct = default);
}
