using System.Globalization;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;

/// <summary>Where a job's listings come from.</summary>
public enum ImportMode
{
    Crawl = 1,
    WebSearch = 2,
    Combined = 3,
    File = 4,
}

/// <summary>
/// What happens to a record that passes validation.
/// </summary>
/// <remarks>
/// <c>AutoPublishValidated</c> is the zero value, so it is what a job gets when nobody chose.
/// That is deliberate: an import whose valid records sit unpublished looks broken, and the
/// records that are genuinely uncertain are held back by validation rather than by this setting.
/// </remarks>
public enum PublishPolicy
{
    AutoPublishValidated = 0,
    ManualReview = 1,
}

public enum ImportJobStatus
{
    Draft = 1,
    Queued = 2,
    Running = 3,
    Paused = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
}

public enum ImportFileFormat
{
    Csv = 1,
    Xlsx = 2,
    Json = 3,
}

/// <summary>Which half of the pipeline a job is in.</summary>
public enum ImportPhase
{
    Discover = 1,
    Process = 2,
    Done = 3,
}

/// <summary>
/// How much work one job may do.
/// </summary>
/// <remarks>
/// Bounded at both ends. The lower bound stops a job that can never find anything; the hard
/// caps stop one administrator's typo from crawling a site for a week.
/// </remarks>
public sealed record ImportLimits(int MaxPages, int MaxDepth, int MaxRecords)
{
    public static ImportLimits Default { get; } = new(200, 2, 1_000);

    public void Validate()
    {
        Assert(MaxPages is >= 1 and <= 5_000, nameof(MaxPages));
        Assert(MaxDepth is >= 0 and <= 5, nameof(MaxDepth));
        Assert(MaxRecords is >= 1 and <= 10_000, nameof(MaxRecords));
    }

    private static void Assert(bool ok, string name)
    {
        if (!ok)
        {
            throw new DomainException(
                "IMPORT_JOB_LIMIT_INVALID", $"{name} is outside the range a job may use.");
        }
    }
}

/// <summary>
/// What the job has done so far.
/// </summary>
/// <remarks>
/// Stored rather than counted from the staged records on demand. A job that discovered 900
/// listings and staged 400 of them has a story in the difference, and the records it rejected
/// as duplicates leave nothing behind to count.
/// </remarks>
public sealed record ImportCounters(
    int Discovered, int Extracted, int Valid, int Invalid, int Duplicate, int Published, int Review)
{
    public static ImportCounters Zero { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Where to pick up if this job stops.
/// </summary>
/// <remarks>
/// <c>ProcessedThroughRecordId</c> is a high-water mark rather than a count, because ids are
/// monotonic and counts are not: a resumed job that counted would re-process whatever had been
/// staged in between.
/// </remarks>
public sealed record ImportCheckpoint(
    ImportPhase Phase, string? AdapterCursor, long ProcessedThroughRecordId)
{
    public static ImportCheckpoint Start { get; } = new(ImportPhase.Discover, null, 0);
}

/// <summary>
/// Which column of an uploaded file means what.
/// </summary>
/// <remarks>
/// Values are a canonical field name, <c>attr:&lt;key&gt;</c> for a catalogue attribute, or
/// <c>ignore</c>. Mapping is explicit rather than guessed: a column silently matched to the
/// wrong field produces a catalogue that looks right and is not.
/// </remarks>
public sealed record ImportColumnMapping(IReadOnlyDictionary<string, string> SourceToField);

public sealed record CreateImportJobSpec(
    ImportMode Mode,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Queries,
    long? TargetCategoryId,
    IReadOnlyList<string> IncludeDomains,
    IReadOnlyList<string> ExcludeDomains,
    ImportLimits Limits,
    PublishPolicy PublishPolicy,
    bool FileRightsAttested);
