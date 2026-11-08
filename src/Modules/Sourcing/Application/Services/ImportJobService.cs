using System.Globalization;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Ports.Inbound;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Sourcing.Application.Services;

/// <summary>
/// Creating and steering import jobs.
/// </summary>
/// <remarks>
/// Every method is load → call the aggregate → audit → one save. The aggregate owns the state
/// machine, so this class has no opinion about which transitions are legal — it translates the
/// aggregate's refusals into errors a caller can act on, and records who asked.
/// </remarks>
internal sealed class ImportJobService(
    IImportJobRepository jobs,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : IImportJobService
{
    private const string TargetType = "ImportJob";

    public async Task<Result<long>> CreateAsync(
        CreateImportJobSpec spec, AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        ImportJob job;
        try
        {
            job = ImportJob.Create(ids.Generate(), spec, actor.UserId, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure<long>(SourcingErrors.ValidationFailed(exception.Message));
        }

        await jobs.AddAsync(job, ct);

        audit.Record(
            actor.ToAudit(), "sourcing.import_job.created", TargetType, Key(job.Id),
            null, Snapshot(job));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(job.Id);
    }

    public Task<Result> StartAsync(long jobId, AdminActor actor, CancellationToken ct = default) =>
        TransitionAsync(jobId, actor, "started", (job, now) => job.Start(now), ct);

    public Task<Result> PauseAsync(long jobId, AdminActor actor, CancellationToken ct = default) =>
        TransitionAsync(jobId, actor, "paused", (job, now) => job.Pause(now), ct);

    /// <remarks>
    /// Resuming bumps the step sequence as well as the status, so any step still in flight from
    /// before the pause is stranded and cannot do a second pass over the same records.
    /// </remarks>
    public Task<Result> ResumeAsync(long jobId, AdminActor actor, CancellationToken ct = default) =>
        TransitionAsync(
            jobId, actor, "resumed",
            (job, now) =>
            {
                job.Resume(now);
                job.BumpStepSequence(now);
            },
            ct);

    public Task<Result> CancelAsync(long jobId, AdminActor actor, CancellationToken ct = default) =>
        TransitionAsync(jobId, actor, "cancelled", (job, now) => job.Cancel(now), ct);

    private async Task<Result> TransitionAsync(
        long jobId, AdminActor actor, string action, Action<ImportJob, DateTimeOffset> change,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) return Result.Failure(SourcingErrors.JobNotFound);

        var before = Snapshot(job);

        try
        {
            change(job, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(FromDomain(exception));
        }

        audit.Record(
            actor.ToAudit(), $"sourcing.import_job.{action}", TargetType, Key(jobId),
            before, Snapshot(job));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <summary>What changed, in the terms an auditor asks about.</summary>
    private static object Snapshot(ImportJob job) => new
    {
        job.Status,
        job.Mode,
        job.Counters,
        Checkpoint = job.Checkpoint.Phase,
        job.StepSequence,
    };

    private static string Key(long jobId) => jobId.ToString(CultureInfo.InvariantCulture);

    /// <remarks>
    /// A job that has no file yet is a <b>state</b> problem, not a malformed request: the
    /// administrator asked a perfectly well-formed question and the answer is "not yet".
    /// </remarks>
    internal static Error FromDomain(DomainException exception) => exception.Code switch
    {
        "IMPORT_JOB_TRANSITION_INVALID" => SourcingErrors.JobStateConflict,
        "IMPORT_JOB_FILE_REQUIRED" => new Error(
            exception.Code, exception.Message, 409),
        "IMPORT_JOB_MAPPING_REQUIRED" => new Error(
            exception.Code, exception.Message, 409),
        _ => SourcingErrors.ValidationFailed(exception.Message),
    };
}
