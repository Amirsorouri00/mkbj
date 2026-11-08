using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// The import job's state machine, which is the part that has to survive a crash.
/// </summary>
/// <remarks>
/// An import runs across many background steps, any of which can be the last one if the process
/// dies. Delivery is at-least-once, so the same step <b>will</b> arrive twice — and a cancelled
/// job that accepted a late step would restart work an administrator believed they had stopped.
/// </remarks>
public sealed class ImportJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Publishing_Valid_Records_Is_What_A_Job_Does_Unless_Told_Otherwise() =>
        // The zero value, so a job gets it when nobody chose. An import whose valid records sit
        // unpublished looks broken; the genuinely uncertain ones are held by validation.
        default(PublishPolicy).Should().Be(PublishPolicy.AutoPublishValidated);

    [Fact]
    public void A_Crawl_Needs_Somewhere_To_Crawl() =>
        Creating(Spec(ImportMode.Crawl, sources: [])).Should().Throw<DomainException>()
            .Which.Code.Should().Be("IMPORT_JOB_SOURCES_REQUIRED");

    [Fact]
    public void A_Search_Needs_Something_To_Search_For() =>
        Creating(Spec(ImportMode.WebSearch, queries: [])).Should().Throw<DomainException>()
            .Which.Code.Should().Be("IMPORT_JOB_QUERIES_REQUIRED");

    [Fact]
    public void Combined_Needs_Both() =>
        Creating(Spec(ImportMode.Combined, sources: ["https://a.example/"], queries: []))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("IMPORT_JOB_QUERIES_REQUIRED");

    [Theory]
    [InlineData("ftp://a.example/")]
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    public void A_Source_Must_Be_An_Absolute_Http_Url(string source) =>
        Creating(Spec(ImportMode.Crawl, sources: [source])).Should().Throw<DomainException>()
            .Which.Code.Should().Be("IMPORT_JOB_SOURCE_INVALID");

    [Theory]
    [InlineData(0, 2, 10)]
    [InlineData(5_001, 2, 10)]
    [InlineData(10, 6, 10)]
    [InlineData(10, 2, 10_001)]
    public void Limits_Are_Bounded_At_Both_Ends(int pages, int depth, int records) =>
        // The lower bound stops a job that can never find anything; the caps stop one typo from
        // crawling a site for a week.
        Creating(Spec(ImportMode.Crawl, limits: new ImportLimits(pages, depth, records)))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("IMPORT_JOB_LIMIT_INVALID");

    [Fact]
    public void A_File_Job_Is_A_Draft_Until_It_Has_A_File_And_A_Mapping()
    {
        var job = ImportJob.Create(1, Spec(ImportMode.File), Guid.NewGuid(), Now);

        job.Status.Should().Be(ImportJobStatus.Draft);

        FluentActions.Invoking(() => job.Start(Now)).Should().Throw<DomainException>()
            .Which.Code.Should().Be("IMPORT_JOB_FILE_REQUIRED");

        job.AttachFile("imports/1/a.csv", ImportFileFormat.Csv, Now);

        FluentActions.Invoking(() => job.Start(Now)).Should().Throw<DomainException>()
            .Which.Code.Should().Be("IMPORT_JOB_MAPPING_REQUIRED");

        job.SetMapping(new ImportColumnMapping(new Dictionary<string, string>()), Now);
        job.Start(Now);

        job.Status.Should().Be(ImportJobStatus.Queued);
    }

    [Fact]
    public void A_Cancelled_Job_Accepts_Nothing_More()
    {
        var job = Running();
        job.Cancel(Now);

        job.AcceptsWork.Should().BeFalse();
        job.IsTerminal.Should().BeTrue();

        FluentActions.Invoking(() => job.Resume(Now)).Should().Throw<DomainException>()
            .Which.Code.Should().Be("IMPORT_JOB_TRANSITION_INVALID");

        // The one that matters: a step still in flight must not restart a stopped job.
        FluentActions.Invoking(() => job.AdvanceCheckpoint(ImportCheckpoint.Start, ImportCounters.Zero, Now))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("IMPORT_JOB_TRANSITION_INVALID");
    }

    [Fact]
    public void Pausing_Stops_Work_And_Resuming_Requeues()
    {
        var job = Running();

        job.Pause(Now);
        job.AcceptsWork.Should().BeFalse("a paused job must not pick up the next step");

        job.Resume(Now);
        job.Status.Should().Be(ImportJobStatus.Queued);
    }

    [Fact]
    public void Resuming_Issues_A_Sequence_That_Strands_Steps_From_Before_The_Pause()
    {
        var job = Running();
        var beforePause = job.AdvanceCheckpoint(
            new ImportCheckpoint(ImportPhase.Process, "c1", 10), ImportCounters.Zero, Now);

        job.Pause(Now);
        job.Resume(Now);

        job.BumpStepSequence(Now).Should().BeGreaterThan(beforePause);
    }

    [Fact]
    public void A_Checkpoint_Advances_The_Sequence_And_Records_What_Was_Done()
    {
        var job = Running();

        job.AdvanceCheckpoint(
            new ImportCheckpoint(ImportPhase.Process, "c1", 99),
            new ImportCounters(10, 9, 0, 0, 1, 0, 0),
            Now)
            .Should().Be(1);

        job.Checkpoint.Should().Be(new ImportCheckpoint(ImportPhase.Process, "c1", 99));
        job.Counters.Discovered.Should().Be(10);
        job.Counters.Duplicate.Should().Be(1);
    }

    [Fact]
    public void Marking_Running_Twice_Does_Not_Move_The_Start_Time()
    {
        var job = ImportJob.Create(1, Spec(ImportMode.Crawl), Guid.NewGuid(), Now);

        job.MarkRunning(Now);
        job.MarkRunning(Now.AddMinutes(5));

        job.StartedAt.Should().Be(Now, "a redelivered step must not make the job look later");
    }

    [Fact]
    public void Completing_Is_Terminal_And_Stamped()
    {
        var job = Running();
        job.Complete(Now);

        job.IsTerminal.Should().BeTrue();
        job.FinishedAt.Should().Be(Now);
        job.Checkpoint.Phase.Should().Be(ImportPhase.Done);
    }

    [Fact]
    public void Domains_Are_Stored_Normalised() =>
        ImportJob.Create(
            1,
            Spec(ImportMode.Crawl) with { ExcludeDomains = ["https://WWW.Bad.Example.com/x"] },
            Guid.NewGuid(),
            Now)
            .ExcludeDomains.Should().Equal("bad.example.com");

    private static ImportJob Running()
    {
        var job = ImportJob.Create(1, Spec(ImportMode.Crawl), Guid.NewGuid(), Now);
        job.MarkRunning(Now);
        return job;
    }

    private static Action Creating(CreateImportJobSpec spec) =>
        () => ImportJob.Create(1, spec, Guid.NewGuid(), Now);

    private static CreateImportJobSpec Spec(
        ImportMode mode,
        IReadOnlyList<string>? sources = null,
        IReadOnlyList<string>? queries = null,
        ImportLimits? limits = null) =>
        new(
            mode,
            sources ?? ["https://shop.example.com/catalogue"],
            queries ?? ["kettles"],
            TargetCategoryId: null,
            IncludeDomains: [],
            ExcludeDomains: [],
            limits ?? ImportLimits.Default,
            PublishPolicy.AutoPublishValidated,
            FileRightsAttested: false);
}
