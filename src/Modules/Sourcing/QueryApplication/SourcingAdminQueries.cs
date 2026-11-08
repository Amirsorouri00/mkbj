using System.Text.Json;
using EwaysVNext.Database;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Outcomes;
using EwaysVNext.Queries.Application.Paginations;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Sourcing.QueryApplication;

public sealed record ImportJobListItemDto(
    long Id,
    string Mode,
    string Status,
    int Discovered,
    int Extracted,
    int Valid,
    int Invalid,
    int Duplicate,
    int Published,
    int Review,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorCode);

public sealed record ImportJobDetailDto(
    ImportJobListItemDto Summary,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Queries,
    long? TargetCategoryId,
    string PublishPolicy,
    string Checkpoint,
    long StepSequence,
    string? FileObjectKey,
    string? FileFormat);

public sealed record StagedRecordListItemDto(
    long Id,
    long LastJobId,
    string SourceUrl,
    string? SourceDomain,
    string Status,
    string MatchKind,
    long? MatchedProductId,
    IReadOnlyList<string> IssueCodes,
    long? PublishedProductId,
    bool PublishSkippedDeactivated,
    DateTimeOffset CreatedAt);

/// <remarks>
/// Carries the normalized document and the issues in full, because a reviewer deciding whether
/// to publish somebody else's words needs to see them, not a summary of them.
/// </remarks>
public sealed record StagedRecordDetailDto(
    StagedRecordListItemDto Summary,
    JsonElement? Normalized,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<FuzzyCandidateRef> Candidates,
    Guid? ReviewedBy,
    Guid? RightsApprovedBy,
    string? ReviewNote);

/// <summary>
/// What the dashboard asks about importing.
/// </summary>
/// <remarks>
/// The "since" fields are named for the window rather than the date so that Admin's gateway can
/// map them without knowing which window was asked for.
/// </remarks>
public sealed record SourcingDashboardCounts(
    int JobsQueued,
    int JobsRunning,
    int JobsPaused,
    int JobsFailedSince,
    int JobsCompletedSince,
    int RecordsNeedsReview,
    int RecordsValidUnpublished,
    int RecordsInvalid,
    int RecordsPublishedSince);

public sealed record StagedRecordFilter(
    long? JobId = null,
    string? Status = null,
    string? SourceDomain = null,
    long? Cursor = null,
    int PageSize = 50);

public interface ISourcingAdminQueries
{
    Task<PaginatedQuery<ImportJobListItemDto>> ListJobsAsync(
        string? status, int page, int pageSize, CancellationToken ct = default);

    Task<Result<ImportJobDetailDto>> GetJobAsync(long jobId, CancellationToken ct = default);

    Task<Result<CursorPageOf<StagedRecordListItemDto>>> ListRecordsAsync(
        StagedRecordFilter filter, CancellationToken ct = default);

    Task<Result<StagedRecordDetailDto>> GetRecordAsync(
        long recordId, CancellationToken ct = default);

    Task<SourcingDashboardCounts> GetDashboardCountsAsync(
        DateTimeOffset since, CancellationToken ct = default);
}

/// <summary>A keyset page, as everywhere else that lists rows which keep arriving.</summary>
public sealed record CursorPageOf<T>(IReadOnlyList<T> Items, long? NextCursor);

internal sealed class SourcingAdminQueries(EwaysVNextDbContext db) : ISourcingAdminQueries
{
    private const int MaxPageSize = 200;

    public async Task<PaginatedQuery<ImportJobListItemDto>> ListJobsAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var current = Math.Max(page, 1);
        var size = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.ImportJobs.AsNoTracking();

        if (status is not null && Enum.TryParse<ImportJobStatus>(status, true, out var parsed))
            query = query.Where(job => job.Status == parsed);

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(job => job.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(job => Summary(job))
            .ToListAsync(ct);

        var pageCount = (int)Math.Ceiling(total / (double)size);

        return new PaginatedQuery<ImportJobListItemDto>(current, size, pageCount, total, rows);
    }

    public async Task<Result<ImportJobDetailDto>> GetJobAsync(
        long jobId, CancellationToken ct = default)
    {
        var job = await db.ImportJobs
            .AsNoTracking()
            .Where(candidate => candidate.Id == jobId)
            .Select(candidate => new ImportJobDetailDto(
                Summary(candidate),
                candidate.Sources,
                candidate.Queries,
                candidate.TargetCategoryId,
                candidate.PublishPolicy.ToString(),
                candidate.Checkpoint.Phase.ToString(),
                candidate.StepSequence,
                candidate.FileObjectKey,
                candidate.FileFormat == null ? null : candidate.FileFormat.ToString()))
            .FirstOrDefaultAsync(ct);

        return job is null
            ? Result.Failure<ImportJobDetailDto>(new Error("NOT_FOUND", "No such import job.", 404))
            : Result.Success(job);
    }

    public async Task<Result<CursorPageOf<StagedRecordListItemDto>>> ListRecordsAsync(
        StagedRecordFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.PageSize is < 1 or > MaxPageSize)
        {
            return Result.Failure<CursorPageOf<StagedRecordListItemDto>>(new Error(
                "VALIDATION_FAILED", $"pageSize must be between 1 and {MaxPageSize}.", 400));
        }

        var query = db.StagedRecords.AsNoTracking();

        if (filter.JobId is { } jobId) query = query.Where(record => record.LastJobId == jobId);

        if (filter.Status is not null
            && Enum.TryParse<StagedRecordStatus>(filter.Status, true, out var status))
        {
            query = query.Where(record => record.Status == status);
        }

        if (filter.SourceDomain is { } domain)
            query = query.Where(record => record.SourceDomain == domain);

        if (filter.Cursor is { } after) query = query.Where(record => record.Id > after);

        // Materialised before mapping: Issues is a backing-field collection the model does not
        // expose as a column, so a projection over it has nothing to translate to. The page is
        // capped at 200 rows, so reading the entities costs little.
        var entities = await query
            .OrderBy(record => record.Id)
            .Take(filter.PageSize + 1)
            .ToListAsync(ct);

        var rows = entities.Select(Summary).ToList();

        var hasMore = rows.Count > filter.PageSize;
        var items = rows.Take(filter.PageSize).ToList();

        return Result.Success(new CursorPageOf<StagedRecordListItemDto>(
            items, hasMore && items.Count > 0 ? items[^1].Id : null));
    }

    public async Task<Result<StagedRecordDetailDto>> GetRecordAsync(
        long recordId, CancellationToken ct = default)
    {
        var record = await db.StagedRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == recordId, ct);

        if (record is null)
        {
            return Result.Failure<StagedRecordDetailDto>(
                new Error("NOT_FOUND", "No such staged record.", 404));
        }

        return Result.Success(new StagedRecordDetailDto(
            Summary(record),
            record.NormalizedJson is null
                ? null
                : JsonDocument.Parse(record.NormalizedJson).RootElement.Clone(),
            record.Issues,
            record.Match.Candidates,
            record.ReviewedBy,
            record.RightsApprovedBy,
            record.ReviewNote));
    }

    public async Task<SourcingDashboardCounts> GetDashboardCountsAsync(
        DateTimeOffset since, CancellationToken ct = default)
    {
        var jobs = await CountByAsync(db.ImportJobs.AsNoTracking(), ct);

        var finishedSince = await CountByAsync(
            db.ImportJobs.AsNoTracking().Where(job => job.FinishedAt >= since), ct);

        var records = await db.StagedRecords
            .AsNoTracking()
            .GroupBy(record => record.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, ct);

        // Counted by when the record last changed, which for a published record is when it was
        // published. Approximate by a hair if a reviewer later edits one, and far cheaper than
        // a separate published-at column nothing else would read.
        var publishedSince = await db.StagedRecords
            .AsNoTracking()
            .CountAsync(record =>
                record.Status == StagedRecordStatus.Published && record.UpdatedAt >= since, ct);

        return new SourcingDashboardCounts(
            jobs.GetValueOrDefault(ImportJobStatus.Queued),
            jobs.GetValueOrDefault(ImportJobStatus.Running),
            jobs.GetValueOrDefault(ImportJobStatus.Paused),
            finishedSince.GetValueOrDefault(ImportJobStatus.Failed),
            finishedSince.GetValueOrDefault(ImportJobStatus.Completed),
            records.GetValueOrDefault(StagedRecordStatus.NeedsReview),
            records.GetValueOrDefault(StagedRecordStatus.Valid),
            records.GetValueOrDefault(StagedRecordStatus.Invalid),
            publishedSince);
    }

    private static Task<Dictionary<ImportJobStatus, int>> CountByAsync(
        IQueryable<ImportJob> jobs, CancellationToken ct) =>
        jobs.GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, ct);

    private static StagedRecordListItemDto Summary(StagedRecord record) => new(
        record.Id,
        record.LastJobId,
        record.SourceUrl,
        record.SourceDomain,
        record.Status.ToString(),
        record.Match.Kind.ToString(),
        record.Match.ProductId,
        [.. record.Issues.Select(issue => issue.Code)],
        record.PublishedProductId,
        record.PublishSkippedDeactivated,
        record.CreatedAt);

    private static ImportJobListItemDto Summary(ImportJob job) => new(
        job.Id,
        job.Mode.ToString(),
        job.Status.ToString(),
        job.Counters.Discovered,
        job.Counters.Extracted,
        job.Counters.Valid,
        job.Counters.Invalid,
        job.Counters.Duplicate,
        job.Counters.Published,
        job.Counters.Review,
        job.CreatedAt,
        job.FinishedAt,
        job.ErrorCode);
}
