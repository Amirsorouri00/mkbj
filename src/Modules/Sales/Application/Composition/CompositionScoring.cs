namespace EwaysVNext.Modules.Sales.Application.Composition;

/// <summary>
/// How good a basket is, when several hit the amount exactly.
/// </summary>
/// <remarks>
/// <para>
/// Every candidate basket here is already <b>correct</b> — it totals the requested amount and
/// every price is inside its band. Scoring is about which correct basket a human would rather
/// receive, so the terms are all about how the invoice reads.
/// </para>
/// <para>
/// Lower is better, and the units are chosen so the first term dominates: a basket that stays
/// near list prices beats a basket that is merely tidy.
/// </para>
/// </remarks>
internal static class CompositionScoring
{
    /// <summary>A target below this is too small for the line-count preference to make sense.</summary>
    private const long LineCountPreferenceFloor = 5_000;

    private const int PreferredLinesLow = 2;
    private const int PreferredLinesHigh = 8;

    /// <summary>Basis points: the deviation term is measured against the target in bps.</summary>
    private const long BasisPoints = 10_000;

    private const long OutsidePreferredLineCountPenalty = 200;
    private const long RepeatedCategoryPenalty = 100;

    /// <summary>Above this, a line's quantity starts to look like a warehouse order.</summary>
    private const long LargeQuantityThreshold = 100;

    public static long Score(
        long targetMinor, long targetUsdMinor, IReadOnlyList<ComposedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetMinor, 0);

        // How far the prices had to move from the catalogue's, in basis points of the target.
        // This dominates on purpose: a partner comparing the invoice against list prices should
        // find the smallest discrepancy we could manage.
        Int128 deviation = 0;
        foreach (var line in lines)
        {
            deviation += (Int128)line.Quantity * Math.Abs(line.UnitPriceMinor - line.ReferenceMinor);
        }

        var score = (long)(deviation * BasisPoints / targetMinor);

        // A two-to-eight line basket reads as a considered order. One line for $5,000 reads as a
        // workaround, and thirty lines reads as a dump. Only applied once the amount is large
        // enough for the preference to mean anything.
        if (targetUsdMinor >= LineCountPreferenceFloor)
        {
            score += OutsidePreferredLineCountPenalty * DistanceOutside(lines.Count);
        }

        // Two lines from the same category look like one product split in half.
        var distinctCategories = lines.Select(line => line.CategoryId).Distinct().Count();
        score += RepeatedCategoryPenalty * (lines.Count - distinctCategories);

        // Very large quantities are legitimate but unusual; nudge away from them without
        // forbidding them.
        foreach (var line in lines)
        {
            score += Math.Max(0, line.Quantity - LargeQuantityThreshold) / 10;
        }

        return score;
    }

    private static long DistanceOutside(int lineCount) =>
        lineCount < PreferredLinesLow ? PreferredLinesLow - lineCount
        : lineCount > PreferredLinesHigh ? lineCount - PreferredLinesHigh
        : 0;
}
