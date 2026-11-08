using EwaysVNext.Jobs;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Jobs;

/// <remarks>
/// The dedupe key is the job and the step, so two requests for one step — a retried API call,
/// say — produce a single queued row.
/// </remarks>
internal sealed class ImportStepScheduler(IJobQueue queue) : IImportStepScheduler
{
    public Task ScheduleAsync(long jobId, long stepSequence, CancellationToken ct = default) =>
        queue.EnqueueAsync(
            JobTypes.ImportJobStep,
            new ImportJobStepPayload(jobId, stepSequence),
            dedupeKey: $"import:{jobId}:{stepSequence}",
            ct: ct);
}
