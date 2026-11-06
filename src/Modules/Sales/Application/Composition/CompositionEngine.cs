using System.Globalization;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Composition;

/// <summary>
/// Composes a basket of real SKUs totalling a partner's requested amount exactly.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately pure — no ports, no IO, no clock, no logger — and
/// <c>SalesArchitectureTests.Sales_Composition_Has_No_IO_Or_Port_Dependencies</c> enforces it.
/// The seed is persisted with the order, so a composition has to be replayable years later from
/// its inputs alone; one ambient dependency and that stops being true.
/// </para>
/// <para>
/// The shape of the search: guess a line count, pick candidates and quantities that get close,
/// leave one quantity-1 line as the anchor that absorbs the remainder, then hand the whole thing
/// to <see cref="ExactHit"/> to land on the amount to the cent. Repeat with different draws,
/// keep the ones that work, and return the one that reads best.
/// </para>
/// <para>
/// Every attempt is independent, so a failure costs one attempt rather than the run. Attempts
/// broaden as they go — wider neighbourhoods, then a relaxed category rule — because the amounts
/// that need a wide search are rare and making every attempt wide would make the common case
/// slow.
/// </para>
/// </remarks>
public static class CompositionEngine
{
    /// <summary>
    /// Persisted with every order, so a basket stays explainable after the strategy changes.
    /// Bump it when the algorithm changes in a way that would compose a different basket from
    /// the same seed.
    /// </summary>
    public const string StrategyVersion = "v1";

    /// <summary>From this attempt, the line count is allowed to drift off its estimate.</summary>
    private const int BroadenLineCountFrom = 32;

    /// <summary>From this attempt, the candidate neighbourhood widens.</summary>
    private const int BroadenNeighbourhoodFrom = 48;

    /// <summary>From this attempt, a category may be used twice in one basket.</summary>
    private const int RelaxCategoryRuleFrom = 64;

    private const int NarrowNeighbourhood = 5;
    private const int WideNeighbourhood = 20;

    /// <summary>At most this many attempt notes are kept — enough to diagnose, not a log.</summary>
    private const int MaxAttemptSummaries = 20;

    private const int MaxFeasibilityRounds = 3;

    public static Result<CompositionPlan> Compose(
        long targetMinor,
        IReadOnlyList<CompositionCandidate> candidates,
        CompositionOptions options,
        long seed,
        ICompositionBudget deadline)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(deadline);

        if (targetMinor <= 0)
        {
            return Result.Failure<CompositionPlan>(
                SalesErrors.ValidationFailed("The amount to compose must be greater than zero."));
        }

        var pool = Normalize(candidates);
        if (pool.Count == 0)
        {
            return Result.Failure<CompositionPlan>(SalesErrors.ExactCompositionUnavailable);
        }

        var categories = pool
            .GroupBy(candidate => candidate.CategoryId)
            .OrderBy(group => group.Key)
            .Select(group => group.ToArray())
            .ToArray();

        var random = new SplitMix64(seed);
        var summaries = new List<string>();
        var solutions = new List<(long Score, int Attempt, ComposedLine[] Lines)>();

        var attempt = 0;
        var exhausted = false;

        for (; attempt < options.MaxAttempts; attempt++)
        {
            if (deadline.IsExhausted)
            {
                exhausted = true;
                break;
            }

            var lines = TryAttempt(
                targetMinor, pool, categories, options, attempt, ref random, deadline, out var note);

            Note(summaries, attempt, note);

            if (lines is null) continue;

            solutions.Add((
                CompositionScoring.Score(targetMinor, options.TargetUsdMinor, lines),
                attempt,
                lines));

            if (solutions.Count >= options.SolutionsToScore) break;
        }

        var stats = new CompositionStats(
            seed, options.StrategyVersion, attempt + 1, pool.Count, summaries);

        if (solutions.Count > 0)
        {
            // Even when the deadline ran out: a correct basket in hand beats a timeout.
            var best = solutions.OrderBy(s => s.Score).ThenBy(s => s.Attempt).First();
            return Result.Success(new CompositionPlan(best.Lines, stats));
        }

        return Result.Failure<CompositionPlan>(
            exhausted ? SalesErrors.CompositionTimeout : SalesErrors.ExactCompositionUnavailable);
    }

    /// <summary>
    /// Sorts and deduplicates the pool so the result depends on the candidates, not on the order
    /// they arrived in.
    /// </summary>
    /// <remarks>
    /// A composition is reproduced from its seed, and a seed only reproduces a basket if the pool
    /// it draws from is the same sequence. The caller's ordering is whatever a query planner felt
    /// like, so it is discarded here rather than trusted.
    /// </remarks>
    private static List<CompositionCandidate> Normalize(IReadOnlyList<CompositionCandidate> candidates)
    {
        var seen = new HashSet<long>();
        var pool = new List<CompositionCandidate>(candidates.Count);

        foreach (var candidate in candidates
                     .Where(c => c.LoMinor >= 1 && c.LoMinor <= c.HiMinor)
                     .OrderBy(c => c.ReferenceMinor).ThenBy(c => c.VariantId))
        {
            if (seen.Add(candidate.VariantId)) pool.Add(candidate);
        }

        return pool;
    }

    /// <summary>One independent attempt, returning its lines or the reason it gave up.</summary>
    private static ComposedLine[]? TryAttempt(
        long targetMinor,
        List<CompositionCandidate> pool,
        CompositionCandidate[][] categories,
        CompositionOptions options,
        int attempt,
        ref SplitMix64 random,
        ICompositionBudget deadline,
        out string note)
    {
        var maxLines = Math.Min(options.MaxLines, pool.Count);
        var lineCount = LineCount(targetMinor, maxLines, attempt, ref random);

        var picks = ChooseLines(
            targetMinor, pool, categories, options, lineCount, attempt, ref random);

        if (picks is null)
        {
            note = Summary(attempt, lineCount, "nolines");
            return null;
        }

        if (!MakeFeasible(targetMinor, picks, options, pool))
        {
            note = Summary(attempt, lineCount, "infeasible");
            return null;
        }

        var drafts = picks
            .Select((pick, index) => new DraftLine(
                index, pick.Quantity, pick.Candidate.ReferenceMinor,
                pick.Candidate.LoMinor, pick.Candidate.HiMinor))
            .ToArray();

        var prices = ExactHit.TryAssign(targetMinor, drafts, deadline);
        if (prices is null)
        {
            note = Summary(attempt, lineCount, "residual");
            return null;
        }

        var lines = picks
            .Select((pick, index) => new ComposedLine(
                pick.Candidate.VariantId, pick.Candidate.CategoryId,
                (int)pick.Quantity, prices[index], pick.Candidate.ReferenceMinor))
            .ToArray();

        // The engine's own output, checked by something that shares no code with it. A failure
        // here is a bug in this method, not a bad request — so the attempt is abandoned and
        // recorded rather than returned.
        if (CompositionVerifier.Verify(targetMinor, pool, lines, options.MaxLines).IsFailure)
        {
            note = Summary(attempt, lineCount, "verify");
            return null;
        }

        note = Summary(attempt, lineCount, "ok");
        return lines;
    }

    /// <summary>
    /// How many lines to aim for: roughly the order of magnitude of the amount, plus a nudge.
    /// </summary>
    /// <remarks>
    /// A $50 order wants one or two lines; a $50,000 order wants a handful. The log is computed
    /// in integers — counting digits and comparing against √10 — because the whole module is
    /// integer-only and a <c>Math.Log10</c> here would be the one floating point value in it.
    /// </remarks>
    private static int LineCount(long targetMinor, int maxLines, int attempt, ref SplitMix64 random)
    {
        var baseCount = Math.Clamp(RoundedLog10(targetMinor / 100) + random.Next(0, 3), 1, maxLines);

        if (attempt < BroadenLineCountFrom) return baseCount;

        // Alternate ±1 every eight attempts, so a target the estimate was wrong about still gets
        // tried at the neighbouring counts.
        var drift = ((attempt - BroadenLineCountFrom) / 8 % 2 == 0) ? 1 : -1;

        return Math.Clamp(baseCount + drift, 1, maxLines);
    }

    /// <summary>
    /// <c>round(log10(value))</c> in integer arithmetic: the digit count less one, rounded up
    /// when the value is at or above √10 × 10^(digits−1).
    /// </summary>
    private static int RoundedLog10(long value)
    {
        if (value <= 0) return 0;

        var digits = 0;
        for (var remaining = value; remaining > 0; remaining /= 10) digits++;

        var exponent = digits - 1;

        // value >= sqrt(10) * 10^exponent  ⇔  value² >= 10^(2·exponent + 1), all in Int128.
        var power = 2 * exponent + 1;
        if (power > 38) return exponent;

        Int128 threshold = 1;
        for (var i = 0; i < power; i++) threshold *= 10;

        return (Int128)value * value >= threshold ? exponent + 1 : exponent;
    }

    private sealed record Pick(CompositionCandidate Candidate, long Quantity)
    {
        public long Quantity { get; set; } = Quantity;
    }

    /// <summary>
    /// Picks the candidates and quantities for one attempt: <c>k−1</c> proportional lines, then
    /// an anchor line of quantity 1 that carries whatever is left.
    /// </summary>
    /// <remarks>
    /// The anchor is what makes exact hits common. A quantity-1 line moves the basket total by
    /// one minor unit per step of its price, so it can absorb any integer remainder on its own —
    /// without it, every residual would need the combination search.
    /// </remarks>
    private static List<Pick>? ChooseLines(
        long targetMinor,
        List<CompositionCandidate> pool,
        CompositionCandidate[][] categories,
        CompositionOptions options,
        int lineCount,
        int attempt,
        ref SplitMix64 random)
    {
        var picks = new List<Pick>(lineCount);
        var usedVariants = new HashSet<long>();
        var usedCategories = new HashSet<long>();
        var categoryOrder = Shuffled(categories.Length, ref random);
        var neighbourhood = attempt >= BroadenNeighbourhoodFrom ? WideNeighbourhood : NarrowNeighbourhood;
        var allowRepeatCategory = attempt >= RelaxCategoryRuleFrom;

        // Weights drawn first, so the shares do not depend on which candidates get picked.
        var weights = new long[Math.Max(1, lineCount - 1)];
        long weightTotal = 0;
        for (var i = 0; i < lineCount - 1; i++)
        {
            weights[i] = random.Next(1000, 3001);
            weightTotal += weights[i];
        }

        var maxReference = pool[^1].ReferenceMinor;
        var cap = Math.Max(
            options.MaxQuantityPerLine,
            (int)Math.Min(int.MaxValue, CeilDiv(targetMinor, Math.Max(1, (long)lineCount * maxReference))));

        for (var i = 0; i < lineCount - 1; i++)
        {
            var share = (long)((Int128)targetMinor * weights[i] / weightTotal);

            var candidate = PickForShare(
                pool, categories, categoryOrder, usedVariants, usedCategories,
                allowRepeatCategory, neighbourhood, share, ref random);

            if (candidate is null) return null;

            var quantity = Math.Clamp(
                Math.Max(1, (share + (candidate.ReferenceMinor / 2)) / candidate.ReferenceMinor),
                1,
                cap);

            picks.Add(new Pick(candidate, quantity));
            usedVariants.Add(candidate.VariantId);
            usedCategories.Add(candidate.CategoryId);
        }

        var anchor = ChooseAnchor(targetMinor, pool, picks, usedVariants, usedCategories);
        if (anchor is null) return null;

        picks.Add(new Pick(anchor, 1));

        return picks;
    }

    /// <summary>
    /// The anchor: a quantity-1 line whose band contains whatever the other lines left over.
    /// </summary>
    /// <remarks>
    /// Preferring an unused category keeps baskets varied. Falling back to the nearest reference
    /// price keeps the attempt alive — the feasibility pass may still rescue it, and an attempt
    /// that gives up here costs the run one of its ninety-six tries.
    /// </remarks>
    private static CompositionCandidate? ChooseAnchor(
        long targetMinor,
        List<CompositionCandidate> pool,
        List<Pick> picks,
        HashSet<long> usedVariants,
        HashSet<long> usedCategories)
    {
        var remainder = targetMinor;
        foreach (var pick in picks) remainder -= pick.Quantity * pick.Candidate.ReferenceMinor;

        var available = pool.Where(c => !usedVariants.Contains(c.VariantId)).ToArray();
        if (available.Length == 0) return null;

        // If the remainder is below anything the pool can price, take a step off the largest
        // quantity so there is something left for the anchor to carry.
        var minimumLo = available.Min(c => c.LoMinor);
        if (remainder < minimumLo && picks.Count > 0)
        {
            var largest = picks.OrderByDescending(p => p.Quantity).First();
            var reduction = CeilDiv(minimumLo - remainder, largest.Candidate.ReferenceMinor);
            var reduced = Math.Max(1, largest.Quantity - reduction);

            remainder += (largest.Quantity - reduced) * largest.Candidate.ReferenceMinor;
            largest.Quantity = reduced;
        }

        var containing = available
            .Where(c => c.LoMinor <= remainder && remainder <= c.HiMinor)
            .ToArray();

        if (containing.Length > 0)
        {
            var preferred = containing.Where(c => !usedCategories.Contains(c.CategoryId)).ToArray();
            return (preferred.Length > 0 ? preferred : containing)
                .OrderBy(c => Math.Abs(c.ReferenceMinor - remainder)).ThenBy(c => c.VariantId).First();
        }

        return available
            .OrderBy(c => Math.Abs(c.ReferenceMinor - remainder)).ThenBy(c => c.VariantId).First();
    }

    /// <summary>
    /// Nudges quantities until the target is inside the basket's reachable range, or gives up.
    /// </summary>
    /// <remarks>
    /// <c>ExactHit</c> can only move prices inside their bands, so a basket whose whole range
    /// misses the target cannot be rescued by pricing. Three rounds, because a fourth almost
    /// never helps and the attempt budget is better spent on a fresh draw.
    /// </remarks>
    private static bool MakeFeasible(
        long targetMinor, List<Pick> picks, CompositionOptions options, List<CompositionCandidate> pool)
    {
        var cap = options.MaxQuantityPerLine;

        for (var round = 0; round < MaxFeasibilityRounds; round++)
        {
            Int128 low = 0, high = 0;
            foreach (var pick in picks)
            {
                low += (Int128)pick.Quantity * pick.Candidate.LoMinor;
                high += (Int128)pick.Quantity * pick.Candidate.HiMinor;
            }

            if (targetMinor >= low && targetMinor <= high) return true;

            // The anchor is the last pick and stays at quantity 1 — it is the line that absorbs
            // the remainder, and changing its quantity would defeat that.
            var adjustable = picks.Take(Math.Max(0, picks.Count - 1)).ToArray();
            if (adjustable.Length == 0) return false;

            if (targetMinor > high)
            {
                var pick = adjustable.OrderByDescending(p => p.Candidate.HiMinor).First();
                var increase = CeilDiv((long)(targetMinor - high), pick.Candidate.HiMinor);
                var raised = Math.Min(cap, pick.Quantity + increase);

                if (raised == pick.Quantity) return false;
                pick.Quantity = raised;
            }
            else
            {
                var pick = adjustable.OrderByDescending(p => p.Quantity).First();
                var decrease = CeilDiv((long)(low - targetMinor), pick.Candidate.LoMinor);
                var lowered = Math.Max(1, pick.Quantity - decrease);

                if (lowered == pick.Quantity) return false;
                pick.Quantity = lowered;
            }
        }

        _ = pool;
        return false;
    }

    /// <summary>
    /// Picks a candidate priced near <paramref name="share"/>, from an unused category where the
    /// rules still require one.
    /// </summary>
    private static CompositionCandidate? PickForShare(
        List<CompositionCandidate> pool,
        CompositionCandidate[][] categories,
        int[] categoryOrder,
        HashSet<long> usedVariants,
        HashSet<long> usedCategories,
        bool allowRepeatCategory,
        int neighbourhood,
        long share,
        ref SplitMix64 random)
    {
        // Four passes, narrowing only when the one before found nothing. A line must not cost
        // more than its share if anything cheaper is free: one $3,250 item picked for a $10
        // share makes the whole basket unreachable, and no amount of price adjustment recovers
        // from it.
        foreach (var atOrBelow in (bool[])[true, false])
        {
            foreach (var index in categoryOrder)
            {
                var category = categories[index];
                if (!allowRepeatCategory && usedCategories.Contains(category[0].CategoryId)) continue;

                var chosen = FromNeighbourhood(
                    category, usedVariants, neighbourhood, share, atOrBelow, ref random);

                if (chosen is not null) return chosen;
            }

            // Every category is used, or none had anything suitable and free.
            var fromPool = FromNeighbourhood(
                pool, usedVariants, neighbourhood, share, atOrBelow, ref random);

            if (fromPool is not null) return fromPool;
        }

        return null;
    }

    /// <summary>
    /// The largest reference at or below the share and its neighbours, one picked at random.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list is sorted by reference price, so "the neighbourhood" is a window ending at the
    /// insertion point. Picking from a window rather than taking the single best is what makes
    /// two attempts with the same line count differ at all.
    /// </para>
    /// <para>
    /// With <paramref name="atOrBelow"/>, only candidates the share can afford are considered —
    /// the caller tries that first, and only widens to anything-at-all when no line can be
    /// afforded anywhere.
    /// </para>
    /// </remarks>
    private static CompositionCandidate? FromNeighbourhood(
        IReadOnlyList<CompositionCandidate> sorted,
        HashSet<long> usedVariants,
        int neighbourhood,
        long share,
        bool atOrBelow,
        ref SplitMix64 random)
    {
        var available = new List<CompositionCandidate>(neighbourhood);
        var at = UpperBound(sorted, share);

        for (var i = at - 1; i >= 0 && available.Count < neighbourhood; i--)
        {
            if (!usedVariants.Contains(sorted[i].VariantId)) available.Add(sorted[i]);
        }

        // Nothing affordable. Only when the caller has allowed it, take the cheapest above
        // instead — a line over its share is still better than no line.
        if (available.Count == 0 && !atOrBelow)
        {
            for (var i = at; i < sorted.Count && available.Count < neighbourhood; i++)
            {
                if (!usedVariants.Contains(sorted[i].VariantId)) available.Add(sorted[i]);
            }
        }

        if (available.Count == 0) return null;

        // Sorted before drawing, so the choice depends on which candidates are available rather
        // than on the order they were collected in.
        available.Sort((left, right) =>
            left.ReferenceMinor != right.ReferenceMinor
                ? left.ReferenceMinor.CompareTo(right.ReferenceMinor)
                : left.VariantId.CompareTo(right.VariantId));

        return available[random.Next(0, available.Count)];
    }

    /// <summary>The first index whose reference price exceeds <paramref name="value"/>.</summary>
    private static int UpperBound(IReadOnlyList<CompositionCandidate> sorted, long value)
    {
        int low = 0, high = sorted.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (sorted[middle].ReferenceMinor <= value) low = middle + 1; else high = middle;
        }

        return low;
    }

    private static int[] Shuffled(int count, ref SplitMix64 random)
    {
        var order = Enumerable.Range(0, count).ToArray();

        for (var i = count - 1; i > 0; i--)
        {
            var j = random.Next(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    private static long CeilDiv(long numerator, long denominator) =>
        denominator <= 0 ? 0 : (numerator + denominator - 1) / denominator;

    private static void Note(List<string> summaries, int attempt, string note)
    {
        summaries.Add(note);
        if (summaries.Count > MaxAttemptSummaries) summaries.RemoveAt(0);

        _ = attempt;
    }

    private static string Summary(int attempt, int lineCount, string outcome) =>
        string.Create(CultureInfo.InvariantCulture, $"a{attempt}:k={lineCount}:{outcome}");
}
