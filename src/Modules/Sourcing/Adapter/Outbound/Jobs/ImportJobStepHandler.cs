using EwaysVNext.Jobs;
using EwaysVNext.Modules.Sourcing.Application.Ports.Inbound;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Jobs;

/// <summary>
/// One step of one import, with the sequence it was issued for.
/// </summary>
/// <remarks>
/// The sequence is what makes at-least-once delivery safe. A redelivered step carries a number
/// the job has already moved past, and is dropped rather than re-running a batch.
/// </remarks>
public sealed record ImportJobStepPayload(long JobId, long StepSequence);

/// <summary>
/// Runs one step and schedules the next.
/// </summary>
/// <remarks>
/// <para>
/// The handler schedules its own successor rather than looping, so each step is a separate lease
/// on the queue. A worker that dies mid-import loses one batch, not the job — and a pause takes
/// effect at the next step rather than whenever the import happens to finish.
/// </para>
/// <para>
/// The next step is enqueued <b>after</b> the pipeline's save has committed, never inside it: a
/// job enqueued in the same transaction would either vanish with a rollback or be claimed by a
/// worker before the rows it refers to exist.
/// </para>
/// </remarks>
public sealed class ImportJobStepHandler(
    IImportPipeline pipeline,
    IJobQueue queue,
    ILogger<ImportJobStepHandler> logger) : IJobHandler<ImportJobStepPayload>
{
    public string Type => JobTypes.ImportJobStep;

    public async Task<JobOutcome> HandleAsync(
        ImportJobStepPayload payload, JobContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var step = await pipeline.RunStepAsync(payload.JobId, payload.StepSequence, ct);

        if (step.IsFailure)
        {
            logger.LogWarning(
                "Import job {JobId} step {Sequence} failed: {Code}.",
                payload.JobId, payload.StepSequence, step.Error!.Code);

            // The job's own state carries the failure; the queue entry itself is done. Retrying
            // here would re-run a step the pipeline has already recorded as finished.
            return JobOutcome.Completed;
        }

        if (!step.Value!.MoreWork)
        {
            logger.LogInformation("Import job {JobId} has no more work.", payload.JobId);
            return JobOutcome.Completed;
        }

        await queue.EnqueueAsync(
            JobTypes.ImportJobStep,
            payload with { StepSequence = payload.StepSequence + 1 },

            // Keyed on the job and the step, so two copies of one redelivered step collapse into
            // a single scheduled successor.
            dedupeKey: $"import:{payload.JobId}:{payload.StepSequence + 1}",
            ct: ct);

        return JobOutcome.Completed;
    }
}
