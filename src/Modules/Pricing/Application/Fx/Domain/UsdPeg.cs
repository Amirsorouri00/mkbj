namespace EwaysVNext.Modules.Pricing.Application.Fx.Domain;

/// <summary>
/// The UAE dirham's fixed peg to the US dollar.
/// <para>
/// A constant rather than a provider rate because it is one: the central bank has held it since
/// 1997, and the ECB does not publish EUR→AED at all. Deriving AED through USD is the only way to
/// quote it, and treating the peg as data to be refreshed would add a staleness failure to a
/// number that does not go stale.
/// </para>
/// </summary>
public static class UsdPeg
{
    public const decimal UsdToAed = 3.6725m;

    public const string Provenance = "derived:usd-peg";
}
