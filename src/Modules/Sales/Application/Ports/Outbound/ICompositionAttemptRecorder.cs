using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Modules.Sales.Application.Domain.Telemetry;

namespace EwaysVNext.Modules.Sales.Application.Ports.Outbound;

/// <summary>
/// Records what one composition run did.
/// </summary>
/// <remarks>
/// <b>Never throws.</b> A failed composition writes no business row at all, so this is the only
/// trace it leaves — but an order must not fail because its telemetry could not be written. The
/// implementation swallows and logs; callers do not guard it.
/// </remarks>
public interface ICompositionAttemptRecorder
{
    Task RecordAsync(CompositionAttemptRecord record, CancellationToken ct = default);
}

/// <summary>
/// How long one composition may run.
/// </summary>
/// <remarks>
/// A factory so a test can substitute a deterministic budget. A composition bounded by the wall
/// clock makes a test's outcome depend on how busy the machine is, which is how a suite acquires
/// a flaky test nobody can reproduce.
/// </remarks>
public interface ICompositionBudgetFactory
{
    ICompositionBudget Start(TimeSpan deadline);
}

/// <summary>
/// Where a composition's seed comes from.
/// </summary>
/// <remarks>
/// A port because the seed is persisted and replayed: a test needs to fix it, and production
/// needs it unpredictable enough that two partners asking for the same amount at the same moment
/// do not get the same basket.
/// </remarks>
public interface ICompositionSeedSource
{
    long NextSeed();
}
