namespace EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;

/// <summary>
/// Asks for a job's next step to be run.
/// </summary>
/// <remarks>
/// A port rather than a direct call to the queue, so Application states the intent — "this job
/// has work" — and the adapter decides how that becomes a queued row. It also keeps the
/// enqueue-after-commit rule in one place the services cannot get wrong.
/// </remarks>
public interface IImportStepScheduler
{
    Task ScheduleAsync(long jobId, long stepSequence, CancellationToken ct = default);
}
