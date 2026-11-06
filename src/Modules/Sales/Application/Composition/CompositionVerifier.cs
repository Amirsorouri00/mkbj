using System.Globalization;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Composition;

/// <summary>
/// Checks a composed basket against the rules, from scratch.
/// </summary>
/// <remarks>
/// <para>
/// This is the last thing that runs before a partner is billed, and it shares no code with the
/// engine on purpose. An engine that checked its own work with its own helpers would agree with
/// itself about a bug in those helpers; the point of this is to disagree.
/// </para>
/// <para>
/// It recomputes everything from the pool and <b>does not trust</b>
/// <see cref="ComposedLine.ReferenceMinor"/> or the line totals. A line that claims a reference
/// price the catalogue never had is exactly the kind of thing worth catching here.
/// </para>
/// <para>
/// Every failure is a 500, not a 4xx: the caller did nothing wrong. A basket that fails
/// verification is this system's bug, and the partner must not be billed for it.
/// </para>
/// </remarks>
public static class CompositionVerifier
{
    private const string Code = "COMPOSITION_VERIFICATION_FAILED";

    public static Result Verify(
        long targetMinor,
        IReadOnlyList<CompositionCandidate> pool,
        IReadOnlyList<ComposedLine> lines,
        int maxLines)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0) return Failure("the basket has no lines");

        if (lines.Count > maxLines)
        {
            return Failure(
                $"the basket has {Text(lines.Count)} lines, more than the {Text(maxLines)} allowed");
        }

        var byVariant = new Dictionary<long, CompositionCandidate>(pool.Count);
        foreach (var candidate in pool) byVariant[candidate.VariantId] = candidate;

        var seen = new HashSet<long>(lines.Count);
        long total = 0;

        foreach (var line in lines)
        {
            if (!seen.Add(line.VariantId))
                return Failure($"variant {Text(line.VariantId)} appears on more than one line");

            if (!byVariant.TryGetValue(line.VariantId, out var candidate))
                return Failure($"variant {Text(line.VariantId)} is not in the candidate pool");

            if (line.Quantity <= 0)
                return Failure($"variant {Text(line.VariantId)} has a quantity of {Text(line.Quantity)}");

            if (line.UnitPriceMinor < candidate.LoMinor || line.UnitPriceMinor > candidate.HiMinor)
            {
                return Failure(
                    $"variant {Text(line.VariantId)} is priced at {Text(line.UnitPriceMinor)}, "
                    + $"outside its band {Text(candidate.LoMinor)}–{Text(candidate.HiMinor)}");
            }

            // Recomputed rather than read off the line, and checked, so an overflow is caught
            // here rather than becoming a negative invoice.
            total = checked(total + (line.Quantity * (long)line.UnitPriceMinor));
        }

        return total == targetMinor
            ? Result.Success()
            : Failure($"the basket totals {Text(total)}, not the requested {Text(targetMinor)}");
    }

    private static Result Failure(string reason) => Result.Failure(new Error(Code, reason, 500));

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
