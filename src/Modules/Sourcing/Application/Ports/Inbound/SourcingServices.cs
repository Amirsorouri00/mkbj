using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sourcing.Application.Ports.Inbound;

/// <summary>What an administrator may do to an import job.</summary>
public interface IImportJobService
{
    Task<Result<long>> CreateAsync(
        CreateImportJobSpec spec, AdminActor actor, CancellationToken ct = default);

    Task<Result> StartAsync(long jobId, AdminActor actor, CancellationToken ct = default);

    Task<Result> PauseAsync(long jobId, AdminActor actor, CancellationToken ct = default);

    Task<Result> ResumeAsync(long jobId, AdminActor actor, CancellationToken ct = default);

    Task<Result> CancelAsync(long jobId, AdminActor actor, CancellationToken ct = default);
}

/// <summary>
/// What an uploaded file looks like, before anyone maps it.
/// </summary>
/// <remarks>
/// A sample of rows comes back with the headings, because a mapping screen that shows only
/// column names makes somebody guess what is in them — and guessing is how a column ends up
/// mapped to the wrong field.
/// </remarks>
public sealed record ImportFilePreview(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> SampleRows,
    int SampleSize);

public interface IImportFileService
{
    /// <summary>Stores the file against a draft job and reports what it contains.</summary>
    Task<Result<ImportFilePreview>> UploadAsync(
        long jobId,
        Stream content,
        ImportFileFormat format,
        AdminActor actor,
        CancellationToken ct = default);

    Task<Result> SetMappingAsync(
        long jobId,
        IReadOnlyDictionary<string, string> mapping,
        AdminActor actor,
        CancellationToken ct = default);
}

/// <summary>Working the review queue.</summary>
public interface IStagedRecordReviewService
{
    Task<Result> ApproveAsync(
        long recordId, long? categoryOverrideId, AdminActor actor, CancellationToken ct = default);

    Task<Result> MergeIntoAsync(
        long recordId, long productId, AdminActor actor, CancellationToken ct = default);

    Task<Result> CreateNewAsync(long recordId, AdminActor actor, CancellationToken ct = default);

    Task<Result> RejectAsync(
        long recordId, string reason, AdminActor actor, CancellationToken ct = default);
}

public interface ISourcePolicyService
{
    Task<Result<long>> CreateAsync(
        string domain,
        bool crawlAllowed,
        bool contentUseAllowed,
        bool imageUseAllowed,
        string? notes,
        AdminActor actor,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(
        long policyId,
        bool crawlAllowed,
        bool contentUseAllowed,
        bool imageUseAllowed,
        string? notes,
        AdminActor actor,
        CancellationToken ct = default);
}

/// <summary>
/// Running one job's worth of work.
/// </summary>
/// <remarks>
/// Driven by the background queue, a batch at a time. Returning the next checkpoint rather than
/// looping to completion is what makes a job pausable: the decision to continue is taken afresh
/// between batches, when a cancellation can be noticed.
/// </remarks>
public interface IImportPipeline
{
    Task<Result<ImportStepResult>> RunStepAsync(
        long jobId, long stepSequence, CancellationToken ct = default);
}

/// <remarks>
/// <c>MoreWork</c> rather than a status, because the caller's only question is whether to
/// schedule another step.
/// </remarks>
public sealed record ImportStepResult(bool MoreWork, ImportCounters Counters);
