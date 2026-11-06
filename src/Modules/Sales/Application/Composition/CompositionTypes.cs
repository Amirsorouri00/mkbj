namespace EwaysVNext.Modules.Sales.Application.Composition;

/// <summary>
/// One sellable variant the engine may put in a basket, with the price band it may be sold at.
/// </summary>
/// <remarks>
/// <para>
/// The band is carried rather than computed here: Pricing owns the ±5% rule and has already
/// applied it, and recomputing it in Sales would mean two implementations of the number a
/// partner is billed.
/// </para>
/// <para>
/// Everything is in minor units. The composition has to hit the partner's amount to the cent, so
/// a floating point type never enters the arithmetic — AGENTS.md rule 11.
/// </para>
/// </remarks>
public sealed record CompositionCandidate(
    long VariantId, long CategoryId, long ReferenceMinor, long LoMinor, long HiMinor);

/// <summary>
/// The knobs one composition run uses.
/// </summary>
/// <remarks>
/// <c>MaxLines</c> and the attempt counts bound the work; <c>TargetUsdMinor</c> is the amount the
/// basket must total. <c>StrategyVersion</c> is persisted with the order, so a basket composed
/// today can be explained by the code that composed it even after the strategy changes.
/// </remarks>
public sealed record CompositionOptions(
    int MaxLines,
    long TargetUsdMinor,
    int MaxQuantityPerLine = 500,
    int SolutionsToScore = 6,
    int MaxAttempts = 96,
    string StrategyVersion = CompositionEngine.StrategyVersion);

/// <summary>One basket line: a variant, how many, and the unit price chosen within its band.</summary>
/// <remarks>
/// <c>ReferenceMinor</c> is carried for the audit trail — what the catalogue price was when this
/// was composed — and is deliberately <b>not</b> trusted by the verifier, which looks it up again.
/// </remarks>
public sealed record ComposedLine(
    long VariantId, long CategoryId, int Quantity, long UnitPriceMinor, long ReferenceMinor)
{
    /// <summary>Checked, because an unnoticed overflow here is an invoice for a negative amount.</summary>
    public long LineTotalMinor => checked(Quantity * UnitPriceMinor);
}

/// <summary>
/// What the run did, kept so a basket can be explained and reproduced.
/// </summary>
/// <remarks>
/// <c>Seed</c> and <c>StrategyVersion</c> together are the reproduction recipe: the engine is
/// pure, so the same seed over the same pool gives the same basket. <c>AttemptSummaries</c> keeps
/// at most the 20 most recent, because the point is diagnosing a failure, not storing a log.
/// </remarks>
public sealed record CompositionStats(
    long Seed,
    string StrategyVersion,
    int Iterations,
    int CandidatePoolSize,
    IReadOnlyList<string> AttemptSummaries);

public sealed record CompositionPlan(IReadOnlyList<ComposedLine> Lines, CompositionStats Stats);

/// <summary>
/// When the engine must stop looking.
/// </summary>
/// <remarks>
/// An interface rather than a <c>TimeSpan</c> so that a test can bound a run deterministically.
/// A time-based budget makes a test's outcome depend on how busy the machine is, which is how a
/// suite acquires a flaky test nobody can reproduce.
/// </remarks>
public interface ICompositionBudget
{
    bool IsExhausted { get; }
}

/// <summary>
/// A wall-clock budget, measured with <see cref="System.Diagnostics.Stopwatch"/> timestamps.
/// </summary>
/// <remarks>
/// Monotonic timestamps rather than <c>DateTimeOffset</c>: an NTP step or a daylight-saving
/// change must not make a composition think it has run for an hour, or for minus one.
/// </remarks>
public sealed class StopwatchBudget(TimeSpan limit) : ICompositionBudget
{
    private readonly long _startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

    public bool IsExhausted =>
        System.Diagnostics.Stopwatch.GetElapsedTime(_startedAt) >= limit;
}

/// <summary>
/// A budget that expires after a fixed number of checks, so a test bounds the engine without
/// depending on the clock or the machine.
/// </summary>
public sealed class IterationBudget(int reads) : ICompositionBudget
{
    private int _reads;

    public bool IsExhausted => Interlocked.Increment(ref _reads) > reads;
}

/// <summary>A budget that is already gone, for the "what happens when there is no time" tests.</summary>
public sealed class ExpiredBudget : ICompositionBudget
{
    public bool IsExhausted => true;
}
