namespace EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;

/// <summary>Where a quoted price came from, which an order line records verbatim.</summary>
public enum PriceSource
{
    /// <summary>The price is already in the asked-for currency; no conversion happened.</summary>
    Base,

    /// <summary>An admin fixed this currency's price directly, bypassing conversion.</summary>
    Override,

    /// <summary>Converted from the base currency at a recorded rate.</summary>
    Fx,
}

/// <summary>
/// Exactly which rate a conversion used, kept so a price can be explained months later.
/// </summary>
/// <remarks>
/// <para>
/// Both ids are nullable and at most one is set: a provider rate has an <c>FxRateId</c>, an
/// admin's override has an <c>FxOverrideId</c>, and a derived rate — the AED peg — has neither
/// because no row produced it.
/// </para>
/// <para>
/// <c>RateAt</c> is the age of the <em>oldest</em> input, not the moment of conversion. A cross
/// rate is only as current as its stalest leg, and saying otherwise would make a two-day-old
/// number look fresh.
/// </para>
/// </remarks>
public sealed record FxSnapshot(
    long? FxRateId,
    long? FxOverrideId,
    string Base,
    string Quote,
    decimal Rate,
    DateTimeOffset RateAt,
    string Provenance);

/// <summary>
/// One variant's price in one currency, with the ±5% band a composed line may move inside.
/// </summary>
/// <remarks>
/// <c>Lo</c> and <c>Hi</c> travel with the reference price rather than being recomputed by the
/// caller, so the band a quote was issued against and the band an order is checked against are
/// the same arithmetic on the same number. <c>Fx</c> is null when no conversion happened — the
/// price was already in this currency, or an admin set it here directly.
/// </remarks>
public sealed record PriceQuote(
    long VariantId,
    string Currency,
    long ReferenceMinor,
    long LoMinor,
    long HiMinor,
    PriceSource Source,
    FxSnapshot? Fx);
