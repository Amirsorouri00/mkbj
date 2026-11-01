namespace EwaysVNext.Modules.Pricing.Application.Bands;

/// <summary>
/// The ±5% window a composed line's unit price must fall inside (PRD §4.2).
/// <para>
/// Integer arithmetic throughout, and never <c>double</c> or <c>decimal</c>. A partner order has
/// to total the requested amount <b>to the cent</b>, and the composition engine searches this
/// band thousands of times per request — a rounding step anywhere in that loop is a cent that
/// goes missing with no way to find it afterwards.
/// </para>
/// </summary>
public static class PriceBand
{
    /// <summary>
    /// The inclusive low and high bounds for a reference price, both in minor units.
    /// <para>
    /// The low bound rounds <em>up</em> and the high bound rounds <em>down</em>, so the band is
    /// never wider than ±5%. Rounding the other way would let a price one cent outside the rule
    /// through, which is the one direction that matters: the band is what the seller promised.
    /// </para>
    /// </summary>
    public static (long Lo, long Hi) For(long referenceMinor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(referenceMinor, 0);

        // checked: a reference price large enough to overflow the multiply is nonsense, and a
        // wrapped band would silently accept any price at all.
        checked
        {
            var lo = (referenceMinor * 95 + 99) / 100;
            var hi = referenceMinor * 105 / 100;
            return (lo, hi);
        }
    }
}
