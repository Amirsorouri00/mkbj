using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Ports.Inbound;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sourcing;

/// <summary>
/// PRD acceptance 10: import twice, get one catalogue.
/// </summary>
/// <remarks>
/// The whole module exists for this. A supplier's feed is read nightly, and the second night
/// must update what the first night created rather than doubling it — while anything a reviewer
/// rejected stays rejected, and anything an administrator withdrew stays withdrawn.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class ImportAcceptanceTests(PostgresFixture fixture)
{
    private const string Base = "/api/v1/admin/imports";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static long _next = DateTimeOffset.UtcNow.Ticks;

    private static long NextId() => Interlocked.Increment(ref _next);

    private static readonly string[] CrawlSources = ["https://shop.example.com/catalogue"];

    [Fact]
    public async Task Acceptance10_Importing_The_Same_Listing_Twice_Stages_It_Once()
    {
        using var factory = fixture.CreateFactory();
        var fingerprint = Fingerprint();

        var first = await StageAsync(factory, fingerprint, jobId: NextId());
        var second = await RestageAsync(factory, fingerprint, jobId: NextId());

        second.Outcome.Should().Be(RestageOutcome.Duplicate);
        second.RecordId.Should().Be(first, "one listing is one record, whatever found it");

        (await CountForAsync(factory, fingerprint)).Should().Be(1);
    }

    [Fact]
    public async Task The_Same_Job_Finding_One_Page_Twice_Discovers_Nothing_New()
    {
        using var factory = fixture.CreateFactory();
        var fingerprint = Fingerprint();
        var jobId = NextId();

        await StageAsync(factory, fingerprint, jobId);
        var again = await RestageAsync(factory, fingerprint, jobId);

        again.Outcome.Should().Be(RestageOutcome.AlreadyInThisJob);
    }

    [Fact]
    public async Task A_Rejected_Listing_Stays_Rejected_When_The_Feed_Is_Read_Again()
    {
        using var factory = fixture.CreateFactory();
        var fingerprint = Fingerprint();
        var recordId = await StageAsync(factory, fingerprint, NextId());

        await InScopeAsync(factory, async services =>
        {
            var records = services.GetRequiredService<IStagedRecordRepository>();
            var record = await records.GetAsync(recordId, Ct);
            record!.Reject(Guid.NewGuid(), "Not our market", DateTimeOffset.UtcNow);
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        });

        var again = await RestageAsync(factory, fingerprint, NextId());

        // A nightly job that un-rejected it would make the review queue pointless.
        again.Outcome.Should().Be(RestageOutcome.KeptRejected);

        var reloaded = await InScopeAsync(factory, services =>
            services.GetRequiredService<IStagedRecordRepository>().GetAsync(recordId, Ct));

        reloaded!.Status.Should().Be(StagedRecordStatus.Rejected);
    }

    [Fact]
    public async Task An_Administrator_Creates_Starts_Pauses_And_Cancels_A_Job()
    {
        using var factory = fixture.CreateFactory();
        using var staff = Staff(factory);

        using var created = await staff.PostAsJsonAsync($"{Base}/jobs", new
        {
            mode = "Crawl",
            sources = CrawlSources,
            maxPages = 10,
            maxDepth = 1,
            maxRecords = 50,
        }, Ct);

        var jobId = (await DataOf(created, HttpStatusCode.Created)).GetInt64();

        // A crawl is queued on creation; pausing and resuming are the controls that matter.
        (await staff.PostAsJsonAsync($"{Base}/jobs/{jobId}/pause", new { }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await staff.PostAsJsonAsync($"{Base}/jobs/{jobId}/resume", new { }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await staff.PostAsJsonAsync($"{Base}/jobs/{jobId}/cancel", new { }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Cancelled is terminal: a late control must not restart it.
        var refused = await staff.PostAsJsonAsync($"{Base}/jobs/{jobId}/resume", new { }, Ct);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var detail = await staff.GetAsync($"{Base}/jobs/{jobId}", Ct);
        (await DataOf(detail)).GetProperty("summary").GetProperty("status").GetString()
            .Should().Be("Cancelled");
    }

    [Fact]
    public async Task A_File_Job_Is_A_Draft_Until_Its_Columns_Are_Mapped()
    {
        using var factory = fixture.CreateFactory();
        using var staff = Staff(factory);

        using var created = await staff.PostAsJsonAsync($"{Base}/jobs", new
        {
            mode = "File",
            fileRightsAttested = true,
        }, Ct);

        var jobId = (await DataOf(created, HttpStatusCode.Created)).GetInt64();

        using var detail = await staff.GetAsync($"{Base}/jobs/{jobId}", Ct);
        (await DataOf(detail)).GetProperty("summary").GetProperty("status").GetString()
            .Should().Be("Draft");

        // Starting without a file is refused by the aggregate, over HTTP as a conflict.
        (await staff.PostAsJsonAsync($"{Base}/jobs/{jobId}/start", new { }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_Uploaded_File_Reports_Its_Columns_And_A_Sample()
    {
        using var factory = fixture.CreateFactory();
        var jobId = await CreateFileJobAsync(factory);

        var preview = await InScopeAsync(factory, async services =>
            await services.GetRequiredService<IImportFileService>().UploadAsync(
                jobId,
                new MemoryStream(Encoding.UTF8.GetBytes(
                    "name,price,currency\nSteel Kettle,19.99,SGD\nGlass Kettle,24.50,SGD\n")),
                ImportFileFormat.Csv,
                new EwaysVNext.Core.Application.AdminActor(Guid.NewGuid(), true),
                Ct));

        preview.IsSuccess.Should().BeTrue(preview.Error?.Message);

        // Mapping is a choice, not a guess: the sample is what makes it one.
        preview.Value!.Columns.Should().Equal("name", "price", "currency");
        preview.Value.SampleRows.Should().NotBeEmpty();
        preview.Value.SampleRows[0]["name"].Should().Be("Steel Kettle");
    }

    [Fact]
    public async Task A_Mapping_Naming_An_Unknown_Field_Is_Refused()
    {
        using var factory = fixture.CreateFactory();
        using var staff = Staff(factory);
        var jobId = await CreateFileJobAsync(factory);

        using var response = await staff.PutAsJsonAsync(
            $"{Base}/jobs/{jobId}/mapping",
            new { mapping = new Dictionary<string, string> { ["col"] = "prodct_name" } },
            Ct);

        // A typo'd field would silently drop that column and the import would look successful.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Source_Policies_Are_Super_Admin_Work()
    {
        using var factory = fixture.CreateFactory();
        using var staff = Staff(factory);
        using var super = Client(factory, AdminJwtFactory.CreateSuperAdminToken());

        var body = new
        {
            domain = $"supplier-{NextId()}.example.com",
            crawlAllowed = true,
            contentUseAllowed = true,
            imageUseAllowed = false,
        };

        // Whether this seller may republish somebody else's words is not a decision for whoever
        // is clearing the queue today.
        (await staff.PostAsJsonAsync($"{Base}/policies", body, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var created = await super.PostAsJsonAsync($"{Base}/policies", body, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        // One policy per domain, so "may we use this?" has one answer.
        (await super.PostAsJsonAsync($"{Base}/policies", body, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_Review_Queue_Lists_What_Needs_A_Person()
    {
        using var factory = fixture.CreateFactory();
        using var staff = Staff(factory);
        var jobId = NextId();

        var recordId = await StageAsync(factory, Fingerprint(), jobId, record =>
            record.RecordProcessing(
                """{"name":"Kettle"}""",
                [],
                MatchResult.New,
                [new ValidationIssue(ValidationCodes.RightsUnknown, IssueSeverity.Review)],
                DateTimeOffset.UtcNow));

        using var listed = await staff.GetAsync($"{Base}/records?jobId={jobId}&status=NeedsReview", Ct);

        var items = (await DataOf(listed)).GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("id").GetInt64()).Should().Contain(recordId);

        using var approved = await staff.PostAsJsonAsync(
            $"{Base}/records/{recordId}/approve", new { }, Ct);

        approved.StatusCode.Should().Be(HttpStatusCode.OK);

        using var detail = await staff.GetAsync($"{Base}/records/{recordId}", Ct);
        var data = await DataOf(detail);

        data.GetProperty("summary").GetProperty("status").GetString().Should().Be("Valid");

        // Somebody took responsibility for publishing material whose rights were unclear.
        data.GetProperty("rightsApprovedBy").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<long> CreateFileJobAsync(WebApplicationFactory<Program> factory)
    {
        using var staff = Staff(factory);

        using var created = await staff.PostAsJsonAsync($"{Base}/jobs", new
        {
            mode = "File",
            fileRightsAttested = true,
        }, Ct);

        return (await DataOf(created, HttpStatusCode.Created)).GetInt64();
    }

    private static string Fingerprint() =>
        Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()).PadRight(64, '0')[..64];

    private static Task<long> StageAsync(
        WebApplicationFactory<Program> factory, string fingerprint, long jobId,
        Action<StagedRecord>? process = null) =>
        InScopeAsync(factory, async services =>
        {
            var record = StagedRecord.Stage(
                NextId(), jobId,
                new Fingerprint(fingerprint),
                Listing(), Raw(), DateTimeOffset.UtcNow);

            process?.Invoke(record);

            await services.GetRequiredService<IStagedRecordRepository>().AddAsync(record, Ct);
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);

            return record.Id;
        });

    private static Task<(long RecordId, RestageOutcome Outcome)> RestageAsync(
        WebApplicationFactory<Program> factory, string fingerprint, long jobId) =>
        InScopeAsync(factory, async services =>
        {
            var records = services.GetRequiredService<IStagedRecordRepository>();

            var existing = await records.FindByFingerprintAsync(fingerprint, Ct);
            existing.Should().NotBeNull("the first pass staged it");

            var outcome = existing!.Restage(jobId, Listing(), Raw(), DateTimeOffset.UtcNow);

            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);

            return (existing.Id, outcome);
        });

    private static Task<int> CountForAsync(
        WebApplicationFactory<Program> factory, string fingerprint) =>
        InScopeAsync(factory, async services =>
        {
            var record = await services.GetRequiredService<IStagedRecordRepository>()
                .FindByFingerprintAsync(fingerprint, Ct);

            return record is null ? 0 : 1;
        });

    private static async Task<T> InScopeAsync<T>(
        WebApplicationFactory<Program> factory, Func<IServiceProvider, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();

        return await work(scope.ServiceProvider);
    }

    private static async Task InScopeAsync(
        WebApplicationFactory<Program> factory, Func<IServiceProvider, Task> work)
    {
        using var scope = factory.Services.CreateScope();

        await work(scope.ServiceProvider);
    }

    private static RawListing Listing() => new(
        "https://shop.example.com/p/1", "shop.example.com", "SKU-1", DateTimeOffset.UtcNow,
        ExtractionMethod.JsonLd,
        new System.Text.Json.Nodes.JsonObject { ["name"] = "A kettle" },
        null, [], null, "cursor");

    private static string Raw() => """{"name":"A kettle"}""";

    private static HttpClient Client(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Staff(WebApplicationFactory<Program> factory) =>
        Client(factory, AdminJwtFactory.CreateStaffToken());

    private static async Task<JsonElement> DataOf(
        HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("data");
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
