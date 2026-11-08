using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sourcing.Application;

/// <summary>
/// Every refusal importing can express.
/// </summary>
/// <remarks>
/// Nothing here is retryable. An import that failed because a file was malformed, a job was in
/// the wrong state or a domain is off limits will fail the same way on a second attempt, and
/// marking any of it retryable would invite a client to hammer it. Transient failures inside
/// the pipeline are the job queue's business, not the caller's.
/// </remarks>
public static class SourcingErrors
{
    public static Error ValidationFailed(string message) => new("VALIDATION_FAILED", message, 400);

    public static readonly Error JobNotFound = new("NOT_FOUND", "No such import job.", 404);

    public static readonly Error RecordNotFound = new("NOT_FOUND", "No such staged record.", 404);

    public static readonly Error FileEmpty = new(
        "IMPORT_FILE_EMPTY", "That file has no rows.", 422);

    public static readonly Error FileHeadingsMissing = new(
        "IMPORT_FILE_HEADINGS_MISSING",
        "The first line must be the column headings.",
        422);

    public static Error FileHeadingDuplicate(string column) => new(
        "IMPORT_FILE_HEADING_DUPLICATE",
        $"The column '{column}' appears more than once, so the mapping would be ambiguous.",
        422);

    public static readonly Error FileFormatUnsupported = new(
        "IMPORT_FILE_FORMAT_UNSUPPORTED",
        "That file format cannot be read.",
        422);

    public static Error FileMalformed(string detail) => new(
        "IMPORT_FILE_MALFORMED", detail, 422);

    /// <summary>The job is not in a state that allows what was asked.</summary>
    public static readonly Error JobStateConflict = new(
        "IMPORT_JOB_STATE_CONFLICT", "The job is not in a state that allows this.", 409);

    public static readonly Error RecordStateConflict = new(
        "STAGED_RECORD_STATE_CONFLICT", "The record is not in a state that allows this.", 409);

    public static readonly Error PolicyDomainTaken = new(
        "SOURCE_POLICY_EXISTS", "There is already a policy for that domain.", 409);
}
