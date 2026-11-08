using System.Text.Json.Nodes;
using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// Staging a listing once, however many times it is found.
/// </summary>
/// <remarks>
/// This is the aggregate PRD acceptance 10 rests on: import the same catalogue twice and get one
/// product, not two. The interesting cases are all about <b>seeing something again</b>.
/// </remarks>
public sealed class StagedRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_Same_Page_Twice_In_One_Crawl_Is_Not_A_Discovery()
    {
        var record = Staged(jobId: 1);

        // A crawl reaching one page by two links has found nothing new.
        record.Restage(1, Listing(), "{}", Now).Should().Be(RestageOutcome.AlreadyInThisJob);
    }

    [Fact]
    public void A_Later_Job_Refreshes_The_Record_Rather_Than_Duplicating_It()
    {
        var record = Published(jobId: 1);

        record.Restage(2, Listing(), """{"name":"v2"}""", Now).Should().Be(RestageOutcome.Duplicate);

        record.Status.Should().Be(StagedRecordStatus.Staged, "the source may have changed");
        record.LastJobId.Should().Be(2);
        record.FirstJobId.Should().Be(1, "where it came from originally is still worth knowing");
        record.RawJson.Should().Be("""{"name":"v2"}""");
    }

    [Fact]
    public void A_Rejection_Outlives_Re_Imports()
    {
        var record = Staged(1);
        record.Reject(Guid.NewGuid(), "Not our market", Now);

        record.Restage(2, Listing(), "{}", Now).Should().Be(RestageOutcome.KeptRejected);

        // The administrator decided about the thing, not about one night's crawl. A nightly job
        // that un-rejected it would make the review queue pointless.
        record.Status.Should().Be(StagedRecordStatus.Rejected);
    }

    [Fact]
    public void An_Error_Means_Invalid() =>
        StagedRecord.DeriveStatus(
            MatchResult.New, [new ValidationIssue(ValidationCodes.PriceInvalid, IssueSeverity.Error)])
            .Should().Be(StagedRecordStatus.Invalid);

    [Fact]
    public void An_Uncertain_Match_Is_A_Review_Item_Even_With_No_Issues() =>
        // Attaching a listing to the wrong existing product is the expensive mistake.
        StagedRecord.DeriveStatus(
            new MatchResult(MatchKind.Fuzzy, null, "title", [new FuzzyCandidateRef(5, "t", 0.7)]), [])
            .Should().Be(StagedRecordStatus.NeedsReview);

    [Fact]
    public void A_Warning_Changes_Nothing() =>
        StagedRecord.DeriveStatus(
            MatchResult.New,
            [new ValidationIssue(ValidationCodes.IdentifierInvalid, IssueSeverity.Warning)])
            .Should().Be(StagedRecordStatus.Valid);

    [Fact]
    public void Errors_Cannot_Be_Approved_Away()
    {
        var record = Processed([new ValidationIssue(ValidationCodes.PriceInvalid, IssueSeverity.Error)]);

        var act = () => record.Approve(Guid.NewGuid(), null, Now);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("STAGED_RECORD_HAS_ERRORS");
    }

    [Fact]
    public void Approving_Settles_The_Review_Questions_And_Keeps_The_Warnings()
    {
        var reviewer = Guid.NewGuid();
        var record = Processed(
        [
            new ValidationIssue(ValidationCodes.RightsUnknown, IssueSeverity.Review),
            new ValidationIssue(ValidationCodes.IdentifierInvalid, IssueSeverity.Warning),
        ]);

        record.Approve(reviewer, null, Now);

        record.Status.Should().Be(StagedRecordStatus.Valid);
        record.ReviewedBy.Should().Be(reviewer);

        // Somebody took responsibility for publishing material whose rights were unclear.
        record.RightsApprovedBy.Should().Be(reviewer);

        record.Issues.Should().ContainSingle()
            .Which.Severity.Should().Be(IssueSeverity.Warning,
                "approving means the questions were decided, not that nothing was odd");
    }

    [Fact]
    public void Approving_Without_A_Rights_Question_Records_No_Rights_Approver()
    {
        var record = Processed([new ValidationIssue(ValidationCodes.FuzzyMatch, IssueSeverity.Review)]);

        record.Approve(Guid.NewGuid(), null, Now);

        record.RightsApprovedBy.Should().BeNull();
    }

    [Fact]
    public void Merging_Names_The_Person_As_The_Evidence()
    {
        var record = Processed([], new MatchResult(MatchKind.Fuzzy, null, "title", []));

        record.MergeInto(Guid.NewGuid(), 42, Now);

        record.Match.Should().Be(new MatchResult(MatchKind.ExactMatch, 42, "admin-merge", []));
        record.Status.Should().Be(StagedRecordStatus.Valid);
    }

    [Fact]
    public void Choosing_To_Create_New_Clears_The_Uncertain_Match()
    {
        var record = Processed([], new MatchResult(MatchKind.Ambiguous, null, "title",
            [new FuzzyCandidateRef(1, "a", 0.6), new FuzzyCandidateRef(2, "b", 0.6)]));

        record.ChooseCreateNew(Guid.NewGuid(), Now);

        record.Match.Should().Be(MatchResult.New);
        record.Status.Should().Be(StagedRecordStatus.Valid);
    }

    [Fact]
    public void A_Published_Record_Is_Withdrawn_Through_The_Product_Not_The_Queue()
    {
        var act = () => Published(1).Reject(Guid.NewGuid(), "changed my mind", Now);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("STAGED_RECORD_ALREADY_PUBLISHED");
    }

    [Fact]
    public void Only_A_Valid_Record_Can_Be_Published()
    {
        var record = Processed([new ValidationIssue(ValidationCodes.PriceInvalid, IssueSeverity.Error)]);

        var act = () => record.MarkPublished(1, [2], false, 3, Now);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("STAGED_RECORD_NOT_PUBLISHABLE");
    }

    [Fact]
    public void A_Failed_Publish_Goes_To_Review_Carrying_Why()
    {
        var record = Processed([]);

        record.MarkPublishFailed("CURRENCY_NOT_ENABLED", Now);

        // The record was fine; something downstream refused it, and a person can fix that.
        record.Status.Should().Be(StagedRecordStatus.NeedsReview);
        record.Issues.Should().Contain(new ValidationIssue(
            ValidationCodes.PublishFailed, IssueSeverity.Review, "CURRENCY_NOT_ENABLED"));
    }

    private static StagedRecord Staged(long jobId) =>
        StagedRecord.Stage(1, jobId, Fingerprint.For(Listing()), Listing(), "{}", Now);

    private static StagedRecord Processed(
        IReadOnlyList<ValidationIssue> issues, MatchResult? match = null)
    {
        var record = Staged(1);
        record.RecordProcessing("{}", [], match ?? MatchResult.New, issues, Now);
        return record;
    }

    private static StagedRecord Published(long jobId)
    {
        var record = StagedRecord.Stage(1, jobId, Fingerprint.For(Listing()), Listing(), "{}", Now);
        record.RecordProcessing("{}", [], MatchResult.New, [], Now);
        record.MarkPublished(100, [200], false, 300, Now);
        return record;
    }

    private static RawListing Listing() => new(
        "https://shop.example.com/p/1",
        "shop.example.com",
        "SKU-1",
        Now,
        ExtractionMethod.JsonLd,
        new JsonObject { ["name"] = "A kettle" },
        null,
        [],
        null,
        "cursor");
}
