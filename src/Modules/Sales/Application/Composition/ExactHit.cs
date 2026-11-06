using System.Numerics;

namespace EwaysVNext.Modules.Sales.Application.Composition;

/// <summary>
/// One line of a draft basket: which candidate, how many, and the band its unit price must stay in.
/// </summary>
internal readonly record struct DraftLine(
    int CandidateIndex, long Quantity, long Reference, long Lo, long Hi);

/// <summary>
/// Chooses a unit price for every line so the basket totals the requested amount <b>exactly</b>.
/// </summary>
/// <remarks>
/// <para>
/// This is where "±5% per unit price" earns its keep. The quantities are already fixed; the only
/// freedom left is where inside each band each unit price sits, and the job is to land the sum on
/// the target to the cent. Integer arithmetic throughout — a floating point step here would
/// produce a basket that is off by a cent in a way no later rounding could fix.
/// </para>
/// <para>
/// Four stages, cheapest first, because most targets are reachable without the last one:
/// </para>
/// <list type="number">
/// <item>spread the difference in proportion to how much room each line has;</item>
/// <item>close what is left greedily, largest quantity first;</item>
/// <item>let a quantity-1 line absorb any integer remainder, if there is one with room;</item>
/// <item>otherwise search for a combination of moves that sums to the remainder.</item>
/// </list>
/// </remarks>
internal static class ExactHit
{
    /// <summary>
    /// How far the residual search may wander from zero.
    /// </summary>
    /// <remarks>
    /// The search is bounded by construction — it only keeps offsets within this window — so a
    /// pathological pool cannot make it run long. With the engine's quantity cap the window is
    /// well under this; the constant is the backstop for a caller that does not cap quantities.
    /// </remarks>
    internal const long MaxResidualWindow = 4_000;

    /// <summary>
    /// The unit prices, or <c>null</c> when this set of lines cannot total the target at all.
    /// </summary>
    /// <remarks>
    /// <c>null</c> is a fact about these quantities, not about the amount: the engine's answer is
    /// to try different quantities, not to give up.
    /// </remarks>
    public static long[]? TryAssign(
        long target, IReadOnlyList<DraftLine> lines, ICompositionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(budget);

        // Int128 because a basket of 200 lines at 500 units of a large price can exceed long
        // while being a perfectly ordinary order.
        Int128 low = 0, high = 0, atReference = 0, weight = 0;
        foreach (var line in lines)
        {
            low += (Int128)line.Quantity * line.Lo;
            high += (Int128)line.Quantity * line.Hi;
            atReference += (Int128)line.Quantity * line.Reference;
            weight += (Int128)line.Quantity * (line.Hi - line.Lo);
        }

        // Outside the reachable range: no choice of prices inside the bands can get there.
        if (target < low || target > high) return null;

        var prices = new long[lines.Count];
        for (var i = 0; i < prices.Length; i++) prices[i] = lines[i].Reference;

        var delta = (long)(target - atReference);

        // (a) Spread the difference in proportion to each line's room, in whole steps of its
        // quantity. Proportional rather than greedy so no single line is pushed to its edge
        // while others sit at their reference — a basket where one SKU is at +5% and the rest
        // are at list price looks like a mistake to whoever reads the invoice.
        if (delta != 0 && weight > 0)
        {
            var spread = delta;
            for (var i = 0; i < prices.Length; i++)
            {
                var line = lines[i];
                var share = (long)((Int128)spread * line.Quantity * (line.Hi - line.Lo) / weight);
                delta -= Move(ref prices[i], line, share / line.Quantity);
            }
        }

        // (b) Close what is left greedily, largest quantity first: each step of a quantity-q line
        // moves the total by q, so the big lines do the coarse work and leave a remainder
        // smaller than the smallest quantity that still has room.
        foreach (var i in Enumerable.Range(0, prices.Length)
                     .OrderByDescending(i => lines[i].Quantity).ThenBy(i => i))
        {
            if (delta == 0) return prices;
            delta -= Move(ref prices[i], lines[i], delta / lines[i].Quantity);
        }

        if (delta == 0) return prices;

        // (c) A quantity-1 line moves the total by exactly one per step, so it can absorb any
        // integer remainder in a single move — the common case, and worth not searching for.
        for (var i = 0; i < prices.Length; i++)
        {
            if (lines[i].Quantity == 1
                && prices[i] + delta >= lines[i].Lo
                && prices[i] + delta <= lines[i].Hi)
            {
                prices[i] += delta;
                return prices;
            }
        }

        // (d) No single line can absorb it. Look for a combination that can.
        return SolveResidual(delta, lines, prices, budget) ? prices : null;
    }

    /// <summary>
    /// Moves one line's price by up to <paramref name="steps"/>, clamped to its band, and
    /// returns how much the basket total moved.
    /// </summary>
    private static long Move(ref long price, DraftLine line, long steps)
    {
        var clamped = Math.Clamp(steps, line.Lo - price, line.Hi - price);
        price += clamped;

        return clamped * line.Quantity;
    }

    /// <summary>
    /// Finds per-line moves whose quantity-weighted sum is exactly the residual.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bounded breadth-first walk over reachable offsets. Each line extends only the states
    /// that existed <b>before</b> that line was processed, so a path uses strictly decreasing
    /// line indices and therefore each line at most once — which is what makes it safe to apply
    /// every move on the reconstructed path without re-checking the bands.
    /// </para>
    /// <para>
    /// The gcd test is a short-circuit, not a correctness guard: if every line's quantity is
    /// even, no combination of them will ever make an odd residual, and the search would grind
    /// through the whole window to discover that. Removing it changes how long the answer takes,
    /// never what the answer is — which is why no test can observe it, and why it is documented
    /// here rather than asserted somewhere misleading.
    /// </para>
    /// </remarks>
    private static bool SolveResidual(
        long residual, IReadOnlyList<DraftLine> lines, long[] prices, ICompositionBudget budget)
    {
        long divisor = 0, largestQuantity = 0;
        for (var i = 0; i < prices.Length; i++)
        {
            if (lines[i].Hi <= lines[i].Lo) continue;

            divisor = (long)BigInteger.GreatestCommonDivisor(divisor, lines[i].Quantity);
            largestQuantity = Math.Max(largestQuantity, lines[i].Quantity);
        }

        // No line has any room left, or the residual is not a multiple of what the quantities
        // can produce.
        if (divisor == 0 || residual % divisor != 0) return false;

        var window = 2 * largestQuantity;
        if (window > MaxResidualWindow) return false;

        var reach = new Dictionary<long, (int Line, long Move, long Previous)> { [0] = (-1, 0, 0) };

        for (var i = 0; i < prices.Length && !reach.ContainsKey(residual); i++)
        {
            if (budget.IsExhausted) return false;

            var line = lines[i];
            var down = Math.Max(line.Lo - prices[i], -((window / line.Quantity) + 1));
            var up = Math.Min(line.Hi - prices[i], (window / line.Quantity) + 1);

            // Snapshot before extending, which is what keeps a line off its own paths.
            foreach (var offset in reach.Keys.Order().ToArray())
            {
                for (var move = down; move <= up; move++)
                {
                    if (move == 0) continue;

                    var next = offset + (move * line.Quantity);
                    if (Math.Abs(next) <= window) reach.TryAdd(next, (i, move, offset));
                }
            }
        }

        if (!reach.ContainsKey(residual)) return false;

        for (var at = residual; at != 0;)
        {
            var (line, move, previous) = reach[at];
            prices[line] += move;
            at = previous;
        }

        return true;
    }
}
