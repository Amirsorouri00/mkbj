namespace EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;

public enum StagedRecordStatus
{
    Staged = 1,
    Valid = 2,
    Invalid = 3,
    NeedsReview = 4,
    Published = 5,
    Rejected = 6,
}

/// <summary>
/// How much a validation finding should stop.
/// </summary>
/// <remarks>
/// Three levels, not two, because the middle one is the whole point of a review queue. An
/// <c>Error</c> is "this cannot be published"; a <c>Review</c> is "a person must decide"; a
/// <c>Warning</c> is worth recording and changes nothing.
/// </remarks>
public enum IssueSeverity
{
    Error = 1,
    Review = 2,
    Warning = 3,
}

public sealed record ValidationIssue(string Code, IssueSeverity Severity, string? Detail = null);

/// <summary>The codes a staged record can carry, in one place so they can be grepped.</summary>
public static class ValidationCodes
{
    public const string NoStructuredData = "NO_STRUCTURED_DATA";
    public const string TitleMissing = "TITLE_MISSING";
    public const string PriceMissing = "PRICE_MISSING";
    public const string PriceInvalid = "PRICE_INVALID";
    public const string CurrencyMissing = "CURRENCY_MISSING";
    public const string BrandMissing = "BRAND_MISSING";
    public const string CategoryMissing = "CATEGORY_MISSING";
    public const string IdentifierInvalid = "IDENTIFIER_INVALID";
    public const string ImageMissing = "IMAGE_MISSING";
    public const string RightsUnknown = "RIGHTS_UNKNOWN";
    public const string RightsContentDenied = "RIGHTS_CONTENT_DENIED";
    public const string RightsImagesDenied = "RIGHTS_IMAGES_DENIED";
    public const string FuzzyMatch = "FUZZY_MATCH";
    public const string AmbiguousMatch = "AMBIGUOUS_MATCH";
    public const string PublishFailed = "PUBLISH_FAILED";
}

public enum MatchKind
{
    New = 1,
    ExactMatch = 2,
    Fuzzy = 3,
    Ambiguous = 4,
}

public sealed record FuzzyCandidateRef(long ProductId, string Title, double Similarity);

/// <summary>
/// What this listing turned out to be in the existing catalogue.
/// </summary>
/// <remarks>
/// <c>MatchedBy</c> names the evidence — a GTIN, an MPN, a title similarity, or
/// <c>admin-merge</c> when a person decided. Months later "why was this attached to that
/// product?" has an answer.
/// </remarks>
public sealed record MatchResult(
    MatchKind Kind, long? ProductId, string? MatchedBy, IReadOnlyList<FuzzyCandidateRef> Candidates)
{
    public static MatchResult New { get; } = new(MatchKind.New, null, null, []);
}

/// <summary>What happened when a listing that already exists was seen again.</summary>
public enum RestageOutcome
{
    /// <summary>First sight, in this job.</summary>
    NewInThisJob,

    /// <summary>This job has already staged it — the same page reached twice in one crawl.</summary>
    AlreadyInThisJob,

    /// <summary>A previous job staged it. The record is refreshed, not duplicated.</summary>
    Duplicate,

    /// <summary>An administrator rejected it before, and that decision stands.</summary>
    KeptRejected,
}
