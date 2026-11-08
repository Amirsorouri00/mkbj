using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;

namespace EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;

/// <summary>
/// One import: where to look, how much to do, and how far it got.
/// </summary>
/// <remarks>
/// <para>
/// The state machine is the aggregate's real job. An import runs for minutes or hours across
/// many background steps, any of which can be the last one if the process dies — so every
/// transition has to be legal from exactly the states it is legal from, and nothing may quietly
/// restart a cancelled job because a queued step arrived late.
/// </para>
/// <para>
/// <c>StepSequence</c> is what makes that safe. Each scheduled step carries the sequence it was
/// issued for, and a step whose sequence is stale is dropped: at-least-once delivery means the
/// same step <b>will</b> arrive twice, and the second copy must not redo the work.
/// </para>
/// </remarks>
public sealed class ImportJob : AggregateRoot<long>
{
    private const int MaxSources = 50;
    private const int MaxQueries = 10;
    private const int MaxQueryLength = 200;

    private ImportJob() { }

    public ImportMode Mode { get; private set; }
    public IReadOnlyList<string> Sources { get; private set; } = [];
    public IReadOnlyList<string> Queries { get; private set; } = [];
    public long? TargetCategoryId { get; private set; }
    public IReadOnlyList<string> IncludeDomains { get; private set; } = [];
    public IReadOnlyList<string> ExcludeDomains { get; private set; } = [];
    public ImportLimits Limits { get; private set; } = ImportLimits.Default;
    public PublishPolicy PublishPolicy { get; private set; }

    /// <summary>Whether whoever uploaded the file said the seller may republish its contents.</summary>
    public bool FileRightsAttested { get; private set; }

    public string? FileObjectKey { get; private set; }
    public ImportFileFormat? FileFormat { get; private set; }
    public ImportColumnMapping? Mapping { get; private set; }

    public ImportJobStatus Status { get; private set; }
    public ImportCounters Counters { get; private set; } = ImportCounters.Zero;
    public ImportCheckpoint Checkpoint { get; private set; } = ImportCheckpoint.Start;
    public long StepSequence { get; private set; }

    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Whether a scheduled step should do anything at all.</summary>
    public bool AcceptsWork => Status is ImportJobStatus.Queued or ImportJobStatus.Running;

    public bool IsTerminal =>
        Status is ImportJobStatus.Completed or ImportJobStatus.Failed or ImportJobStatus.Cancelled;

    /// <remarks>
    /// A file job starts as a Draft because it is not yet runnable: the file has still to be
    /// uploaded and its columns mapped. Every other mode has everything it needs at creation,
    /// so it is queued immediately.
    /// </remarks>
    public static ImportJob Create(
        long id, CreateImportJobSpec spec, Guid createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(spec);

        spec.Limits.Validate();

        var sources = Normalize(spec.Sources);
        var queries = Normalize(spec.Queries);

        if (spec.Mode is ImportMode.Crawl or ImportMode.Combined && sources.Count == 0)
            throw new DomainException("IMPORT_JOB_SOURCES_REQUIRED", "This mode needs a source URL.");

        if (spec.Mode is ImportMode.WebSearch or ImportMode.Combined && queries.Count == 0)
            throw new DomainException("IMPORT_JOB_QUERIES_REQUIRED", "This mode needs a search query.");

        if (sources.Count > MaxSources)
            throw new DomainException("IMPORT_JOB_SOURCES_REQUIRED", "Too many sources.");

        foreach (var source in sources) AssertSource(source);

        if (queries.Count > MaxQueries) throw new DomainException(
            "IMPORT_JOB_QUERIES_REQUIRED", "Too many queries.");

        foreach (var query in queries)
        {
            if (query.Length > MaxQueryLength)
                throw new DomainException("IMPORT_JOB_QUERIES_REQUIRED", "A query is too long.");
        }

        return new ImportJob
        {
            Id = id,
            Mode = spec.Mode,
            Sources = sources,
            Queries = queries,
            TargetCategoryId = spec.TargetCategoryId,
            IncludeDomains = [.. Normalize(spec.IncludeDomains).Select(SourceDomain.Normalize)],
            ExcludeDomains = [.. Normalize(spec.ExcludeDomains).Select(SourceDomain.Normalize)],
            Limits = spec.Limits,
            PublishPolicy = spec.PublishPolicy,
            FileRightsAttested = spec.FileRightsAttested,
            Status = spec.Mode == ImportMode.File ? ImportJobStatus.Draft : ImportJobStatus.Queued,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }

    public void AttachFile(string objectKey, ImportFileFormat format, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        Require(Status == ImportJobStatus.Draft);

        FileObjectKey = objectKey;
        FileFormat = format;
        UpdatedAt = now;
    }

    public void SetMapping(ImportColumnMapping mapping, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        Require(Status == ImportJobStatus.Draft);

        Mapping = mapping;
        UpdatedAt = now;
    }

    public void Start(DateTimeOffset now)
    {
        Require(Status == ImportJobStatus.Draft);

        if (Mode == ImportMode.File && FileObjectKey is null)
            throw new DomainException("IMPORT_JOB_FILE_REQUIRED", "Upload the file first.");

        if (Mode == ImportMode.File && Mapping is null)
            throw new DomainException("IMPORT_JOB_MAPPING_REQUIRED", "Map the columns first.");

        Status = ImportJobStatus.Queued;
        UpdatedAt = now;
    }

    /// <remarks>
    /// Idempotent, and <c>StartedAt</c> is set once. A step that arrives twice must not make the
    /// job look as though it started later than it did.
    /// </remarks>
    public void MarkRunning(DateTimeOffset now)
    {
        Require(Status is ImportJobStatus.Queued or ImportJobStatus.Running);

        Status = ImportJobStatus.Running;
        StartedAt ??= now;
        UpdatedAt = now;
    }

    public void Pause(DateTimeOffset now)
    {
        Require(Status is ImportJobStatus.Queued or ImportJobStatus.Running);

        Status = ImportJobStatus.Paused;
        UpdatedAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        Require(Status == ImportJobStatus.Paused);

        Status = ImportJobStatus.Queued;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Require(!IsTerminal);

        Status = ImportJobStatus.Cancelled;
        FinishedAt = now;
        UpdatedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        Require(Status == ImportJobStatus.Running);

        Status = ImportJobStatus.Completed;
        Checkpoint = Checkpoint with { Phase = ImportPhase.Done };
        FinishedAt = now;
        UpdatedAt = now;
    }

    public void Fail(string code, string message, DateTimeOffset now)
    {
        Require(!IsTerminal);

        Status = ImportJobStatus.Failed;
        ErrorCode = code;
        ErrorMessage = message;
        FinishedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records progress and issues the sequence the next step must carry.
    /// </summary>
    /// <remarks>
    /// Running only. A paused or cancelled job that still accepted checkpoints would advance
    /// while an administrator believed it had stopped.
    /// </remarks>
    public long AdvanceCheckpoint(
        ImportCheckpoint next, ImportCounters counters, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(counters);
        Require(Status == ImportJobStatus.Running);

        Checkpoint = next;
        Counters = counters;
        UpdatedAt = now;

        return ++StepSequence;
    }

    /// <summary>
    /// Issues a fresh sequence without moving the checkpoint.
    /// </summary>
    /// <remarks>
    /// Used when resuming: any step still in flight from before the pause carries an older
    /// sequence and is dropped on arrival.
    /// </remarks>
    public long BumpStepSequence(DateTimeOffset now)
    {
        UpdatedAt = now;

        return ++StepSequence;
    }

    private static void AssertSource(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new DomainException(
                "IMPORT_JOB_SOURCE_INVALID", $"'{source}' is not an absolute http(s) URL.");
        }
    }

    private static List<string> Normalize(IReadOnlyList<string>? values) =>
        [.. (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static void Require(bool legal)
    {
        if (!legal)
        {
            throw new DomainException(
                "IMPORT_JOB_TRANSITION_INVALID", "The job is not in a state that allows this.");
        }
    }
}
