using System.Text.Json;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Ports.Inbound;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Sourcing.Application.Pipeline;

/// <summary>
/// One batch of one job's work.
/// </summary>
/// <remarks>
/// <para>
/// <b>A step does a batch and returns, rather than looping to completion.</b> That is what makes
/// a job pausable and cancellable at all: the decision to continue is taken afresh between
/// batches, where a cancellation can be noticed. A step that ran to the end would mean "pause"
/// took effect whenever the import happened to finish.
/// </para>
/// <para>
/// <c>stepSequence</c> is checked against the job before anything happens. Delivery is
/// at-least-once, so the same step arrives twice; the second copy carries a sequence the job has
/// moved past and does nothing. Without that, a redelivered step re-processes a batch and the
/// counters drift.
/// </para>
/// </remarks>
public sealed class ImportPipeline(
    IImportJobRepository jobs,
    IStagedRecordRepository records,
    RecordProcessor processor,
    RecordPublisher publisher,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<ImportPipeline> logger) : IImportPipeline
{
    /// <summary>
    /// How many records one step handles.
    /// </summary>
    /// <remarks>
    /// Small enough that a pause is noticed quickly, large enough that the per-step overhead is
    /// not the dominant cost.
    /// </remarks>
    public const int BatchSize = 50;

    public async Task<Result<ImportStepResult>> RunStepAsync(
        long jobId, long stepSequence, CancellationToken ct = default)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) return Result.Failure<ImportStepResult>(SourcingErrors.JobNotFound);

        if (!job.AcceptsWork)
        {
            // Paused, cancelled or finished. Not an error — a late step arriving after an
            // administrator stopped the job is exactly what this check is for.
            return Result.Success(new ImportStepResult(false, job.Counters));
        }

        if (stepSequence < job.StepSequence)
        {
            logger.LogInformation(
                "Dropping step {Sequence} for job {JobId}; the job is at {Current}.",
                stepSequence, jobId, job.StepSequence);

            return Result.Success(new ImportStepResult(false, job.Counters));
        }

        var now = clock.UtcNow;
        job.MarkRunning(now);

        var batch = await records.NextForProcessingAsync(
            jobId, job.Checkpoint.ProcessedThroughRecordId, BatchSize, ct);

        if (batch.Count == 0)
        {
            job.Complete(now);
            await unitOfWork.SaveChangesAsync(ct);

            return Result.Success(new ImportStepResult(false, job.Counters));
        }

        var counters = job.Counters;
        var lastId = job.Checkpoint.ProcessedThroughRecordId;

        foreach (var record in batch)
        {
            ct.ThrowIfCancellationRequested();

            counters = await ProcessOneAsync(job, record, counters, ct);
            lastId = record.Id;
        }

        job.AdvanceCheckpoint(
            job.Checkpoint with { Phase = ImportPhase.Process, ProcessedThroughRecordId = lastId },
            counters,
            now);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ImportStepResult(true, counters));
    }

    private async Task<ImportCounters> ProcessOneAsync(
        ImportJob job, StagedRecord record, ImportCounters counters, CancellationToken ct)
    {
        var listing = JsonSerializer.Deserialize<RawListing>(record.RawJson)
            ?? throw new InvalidOperationException(
                $"Staged record {record.Id} has no readable raw listing.");

        var processed = await processor.ProcessAsync(
            record,
            listing,
            job.FileRightsAttested,
            job.Id,
            requireCategory: job.TargetCategoryId is null,
            adminChoice: null,
            ct);

        record.RecordProcessing(
            processed.ToJson(), record.ImageKeys, processed.Match, processed.Issues, clock.UtcNow);

        counters = counters with { Extracted = counters.Extracted + 1 };

        switch (record.Status)
        {
            case StagedRecordStatus.Invalid:
                return counters with { Invalid = counters.Invalid + 1 };

            case StagedRecordStatus.NeedsReview:
                return counters with { Review = counters.Review + 1 };

            case StagedRecordStatus.Valid:
                counters = counters with { Valid = counters.Valid + 1 };
                break;

            default:
                return counters;
        }

        // A job set to manual review leaves valid records for a person. The records are valid;
        // the seller has simply chosen to look before publishing.
        if (job.PublishPolicy == PublishPolicy.ManualReview) return counters;

        return await PublishOneAsync(record, processed, counters, ct);
    }

    private async Task<ImportCounters> PublishOneAsync(
        StagedRecord record, ProcessedRecord processed, ImportCounters counters,
        CancellationToken ct)
    {
        var categoryPath = processed.Product.Category is { } category
            ? new[] { category }
            : [];

        var published = await publisher.PublishAsync(record, processed, categoryPath, ct);

        if (published.IsFailure)
        {
            record.MarkPublishFailed(published.Error!.Code, clock.UtcNow);

            return counters with { Review = counters.Review + 1 };
        }

        var outcome = published.Value!;

        record.MarkPublished(
            outcome.ProductId, outcome.VariantIds, outcome.SkippedBecauseDeactivated,
            outcome.SourceListingId, clock.UtcNow);

        foreach (var warning in outcome.Warnings)
        {
            logger.LogWarning(
                "Record {RecordId} published as product {ProductId} but reported {Warning}.",
                record.Id, outcome.ProductId, warning);
        }

        return counters with { Published = counters.Published + 1 };
    }
}
