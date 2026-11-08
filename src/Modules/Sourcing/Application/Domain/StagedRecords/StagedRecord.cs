using System.Security.Cryptography;
using System.Text;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Discovery;

namespace EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;

/// <summary>
/// One listing, staged once, however many times it is found.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate exists so that importing the same catalogue twice produces one product rather
/// than two. Its identity is the fingerprint, and everything else — which job saw it last, what
/// it normalized to, which product it matched, what a reviewer decided — hangs off that.
/// </para>
/// <para>
/// <b>A rejection outlives re-imports.</b> An administrator who rejected a listing has made a
/// decision about the thing itself, not about one night's crawl, and a nightly job that quietly
/// un-rejected it would make the review queue pointless.
/// </para>
/// </remarks>
public sealed class StagedRecord : AggregateRoot<long>
{
    private readonly List<ValidationIssue> _issues = [];

    private StagedRecord() { }

    public string Fingerprint { get; private set; } = string.Empty;

    /// <summary>The job that first saw it, kept even as later jobs refresh it.</summary>
    public long FirstJobId { get; private set; }

    public long LastJobId { get; private set; }

    public string SourceUrl { get; private set; } = string.Empty;
    public string? SourceDomain { get; private set; }

    public string RawJson { get; private set; } = "{}";

    /// <summary>A hash of the raw document, so "did anything change?" is one comparison.</summary>
    public string RawHash { get; private set; } = string.Empty;

    public string? NormalizedJson { get; private set; }
    public IReadOnlyList<string> ImageKeys { get; private set; } = [];

    public MatchResult Match { get; private set; } = MatchResult.New;
    public IReadOnlyList<ValidationIssue> Issues => _issues;

    public StagedRecordStatus Status { get; private set; }

    public Guid? ReviewedBy { get; private set; }
    public string? ReviewNote { get; private set; }
    public long? CategoryOverrideId { get; private set; }

    /// <summary>Who took responsibility for publishing material whose rights were unclear.</summary>
    public Guid? RightsApprovedBy { get; private set; }

    public long? PublishedProductId { get; private set; }
    public IReadOnlyList<long> PublishedVariantIds { get; private set; } = [];

    /// <summary>
    /// True when publishing deliberately left an existing product alone.
    /// </summary>
    /// <remarks>
    /// An administrator who deactivated a product has made a decision, and a re-import must not
    /// overturn it. Recorded rather than silent, so the queue can explain why nothing happened.
    /// </remarks>
    public bool PublishSkippedDeactivated { get; private set; }

    public long? SourceListingId { get; private set; }
    public DateTimeOffset? StagingCleanedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static StagedRecord Stage(
        long id, long jobId, Fingerprint fingerprint, RawListing listing, string rawJson,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(listing);

        return new StagedRecord
        {
            Id = id,
            Fingerprint = fingerprint.Value,
            FirstJobId = jobId,
            LastJobId = jobId,
            SourceUrl = listing.SourceUrl,
            SourceDomain = listing.SourceDomain,
            RawJson = rawJson,
            RawHash = HashOf(rawJson),
            Status = StagedRecordStatus.Staged,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// The same listing, seen again.
    /// </summary>
    /// <remarks>
    /// Within one job this is a no-op: a crawl reaching the same page by two links has not
    /// discovered anything. From a later job the record is refreshed and sent back through the
    /// pipeline, because the source may have changed its price or its description.
    /// </remarks>
    public RestageOutcome Restage(
        long jobId, RawListing listing, string rawJson, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(listing);

        if (Status == StagedRecordStatus.Rejected) return RestageOutcome.KeptRejected;

        if (jobId == LastJobId) return RestageOutcome.AlreadyInThisJob;

        LastJobId = jobId;
        SourceUrl = listing.SourceUrl;
        SourceDomain = listing.SourceDomain;
        RawJson = rawJson;
        RawHash = HashOf(rawJson);
        NormalizedJson = null;
        _issues.Clear();
        Match = MatchResult.New;
        Status = StagedRecordStatus.Staged;
        UpdatedAt = now;

        return RestageOutcome.Duplicate;
    }

    public void RecordProcessing(
        string normalizedJson,
        IReadOnlyList<string> imageKeys,
        MatchResult match,
        IReadOnlyList<ValidationIssue> issues,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(issues);

        NormalizedJson = normalizedJson;
        ImageKeys = [.. imageKeys];
        Match = match;

        _issues.Clear();
        _issues.AddRange(issues);

        Status = DeriveStatus(match, issues);
        UpdatedAt = now;
    }

    /// <summary>
    /// What a record's findings add up to.
    /// </summary>
    /// <remarks>
    /// Static and pure so the pipeline, the review service and a test all reach the same verdict
    /// from the same inputs. An uncertain <b>match</b> is a review item as much as an uncertain
    /// field is: attaching a listing to the wrong existing product is the expensive mistake.
    /// </remarks>
    public static StagedRecordStatus DeriveStatus(
        MatchResult match, IReadOnlyList<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(issues);

        if (issues.Any(issue => issue.Severity == IssueSeverity.Error))
            return StagedRecordStatus.Invalid;

        if (issues.Any(issue => issue.Severity == IssueSeverity.Review)
            || match.Kind is MatchKind.Fuzzy or MatchKind.Ambiguous)
        {
            return StagedRecordStatus.NeedsReview;
        }

        return StagedRecordStatus.Valid;
    }

    public void MarkPublished(
        long productId,
        IReadOnlyList<long> variantIds,
        bool skippedBecauseDeactivated,
        long sourceListingId,
        DateTimeOffset now)
    {
        if (Status != StagedRecordStatus.Valid)
        {
            throw new DomainException(
                "STAGED_RECORD_NOT_PUBLISHABLE",
                $"A record that is {Status} cannot be published.");
        }

        PublishedProductId = productId;
        PublishedVariantIds = [.. variantIds];
        PublishSkippedDeactivated = skippedBecauseDeactivated;
        SourceListingId = sourceListingId;
        Status = StagedRecordStatus.Published;
        UpdatedAt = now;
    }

    /// <remarks>
    /// A publishing failure is a review item, not an error: the record itself was fine, and
    /// whatever refused it — a disabled currency, a missing category — is something a person
    /// can fix and retry.
    /// </remarks>
    public void MarkPublishFailed(string errorCode, DateTimeOffset now)
    {
        _issues.Add(new ValidationIssue(
            ValidationCodes.PublishFailed, IssueSeverity.Review, errorCode));

        Status = StagedRecordStatus.NeedsReview;
        UpdatedAt = now;
    }

    /// <summary>
    /// A reviewer says this may go ahead.
    /// </summary>
    /// <remarks>
    /// Clears the <c>Review</c> findings and keeps the <c>Warning</c> ones: approving means "I
    /// have decided these questions", not "pretend nothing was odd". Errors cannot be approved
    /// away at all — there is nothing to publish.
    /// </remarks>
    public void Approve(Guid reviewer, long? categoryOverrideId, DateTimeOffset now)
    {
        if (_issues.Any(issue => issue.Severity == IssueSeverity.Error))
        {
            throw new DomainException(
                "STAGED_RECORD_HAS_ERRORS", "Fix the errors before approving this record.");
        }

        if (_issues.Any(issue => issue.Code == ValidationCodes.RightsUnknown))
            RightsApprovedBy = reviewer;

        _issues.RemoveAll(issue => issue.Severity == IssueSeverity.Review);

        ReviewedBy = reviewer;
        CategoryOverrideId = categoryOverrideId ?? CategoryOverrideId;
        Status = DeriveStatus(Match, _issues);
        UpdatedAt = now;
    }

    /// <summary>A reviewer says this listing is an existing product.</summary>
    public void MergeInto(Guid reviewer, long productId, DateTimeOffset now)
    {
        Match = new MatchResult(MatchKind.ExactMatch, productId, "admin-merge", []);

        Approve(reviewer, null, now);
    }

    /// <summary>A reviewer says this listing is something the catalogue does not have.</summary>
    public void ChooseCreateNew(Guid reviewer, DateTimeOffset now)
    {
        Match = MatchResult.New;

        Approve(reviewer, null, now);
    }

    public void Reject(Guid reviewer, string reason, DateTimeOffset now)
    {
        if (Status == StagedRecordStatus.Published)
        {
            throw new DomainException(
                "STAGED_RECORD_ALREADY_PUBLISHED",
                "This record is already a product; withdraw the product instead.");
        }

        ReviewedBy = reviewer;
        ReviewNote = reason;
        Status = StagedRecordStatus.Rejected;
        UpdatedAt = now;
    }

    public void MarkStagingCleaned(DateTimeOffset now)
    {
        ImageKeys = [];
        StagingCleanedAt = now;
        UpdatedAt = now;
    }

    private static string HashOf(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
