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
