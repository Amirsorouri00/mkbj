using System.Text;
using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.IntegrationTests.Support;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Ports.Inbound;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Modules.Sourcing.QueryApplication;
using EwaysVNext.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sourcing;

/// <summary>
/// A spreadsheet becomes products, and a second upload of it does not.
/// </summary>
/// <remarks>
/// The file path is driven end to end here — upload, map, start, step — because the pieces are
/// individually tested and what is left to get wrong is how they fit: whether the cursor
/// advances, whether discovery hands over to processing, and whether a re-import adds anything.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class FileImportEndToEndTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly AdminActor Admin = new(Guid.NewGuid(), IsSuperAdmin: true);

    private const string Csv = """
        name,brand,price,currency,gtin,category
        Steel Kettle 1.7L,Acme,19.99,SGD,4006381333931,Kitchen
        Glass Kettle 1.5L,Acme,24.50,SGD,,Kitchen
        ,Acme,9.99,SGD,,Kitchen
        """;

    private static readonly Dictionary<string, string> Mapping = new()
    {
        ["name"] = "name",
        ["brand"] = "brand",
        ["price"] = "price",
        ["currency"] = "currency",
        ["gtin"] = "gtin",
        ["category"] = "category",
    };

    [Fact]
    public async Task A_Spreadsheet_Becomes_Staged_Records_And_The_Bad_Row_Is_Held_Back()
    {
        using var factory = fixture.CreateFactory();
        await StorefrontScenario.CreateAsync(
            factory.Services.CreateScope().ServiceProvider, Ct);

        var jobId = await RunImportAsync(factory);

        var records = await RecordsForAsync(factory, jobId);

        records.Should().HaveCount(3, "every row is staged, including the one that cannot publish");

        // The row with no name has nothing to make a product from.
        records.Should().Contain(record => record.Status == nameof(StagedRecordStatus.Invalid));

        // The two complete rows reached a conclusion rather than sitting unprocessed.
        records.Should().NotContain(record => record.Status == nameof(StagedRecordStatus.Staged));
    }

    [Fact]
    public async Task Importing_The_Same_Spreadsheet_Again_Adds_No_Records()
    {
        using var factory = fixture.CreateFactory();
        await StorefrontScenario.CreateAsync(
            factory.Services.CreateScope().ServiceProvider, Ct);

        await RunImportAsync(factory);
        var before = await TotalRecordsAsync(factory);

        // A different job, the same file: the fingerprints are the rows' contents, so nothing
        // new is staged however often the supplier re-sends the catalogue.
        await RunImportAsync(factory);

        (await TotalRecordsAsync(factory)).Should().Be(before);
    }

    [Fact]
    public async Task A_Cancelled_Job_Stops_Taking_Steps()
    {
        using var factory = fixture.CreateFactory();
        var jobId = await CreateAndStartAsync(factory);

        await InScopeAsync(factory, services =>
            services.GetRequiredService<IImportJobService>().CancelAsync(jobId, Admin, Ct));

        var step = await InScopeAsync(factory, services =>
            services.GetRequiredService<IImportPipeline>().RunStepAsync(jobId, 99, Ct));

        // A step still in flight when an administrator stopped the job must do nothing.
        step.Value!.MoreWork.Should().BeFalse();

        (await RecordsForAsync(factory, jobId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Stale_Step_Is_Dropped_Rather_Than_Re_Running_A_Batch()
    {
        using var factory = fixture.CreateFactory();
        var jobId = await RunImportAsync(factory);

        var counters = await InScopeAsync(factory, async services =>
            (await services.GetRequiredService<ISourcingAdminQueries>()
                .GetJobAsync(jobId, Ct)).Value!.Summary);

        // Sequence 0 is long past. Delivery is at-least-once, so this will happen.
        var replay = await InScopeAsync(factory, services =>
            services.GetRequiredService<IImportPipeline>().RunStepAsync(jobId, 0, Ct));

        replay.Value!.MoreWork.Should().BeFalse();

        var after = await InScopeAsync(factory, async services =>
            (await services.GetRequiredService<ISourcingAdminQueries>()
                .GetJobAsync(jobId, Ct)).Value!.Summary);

        after.Discovered.Should().Be(counters.Discovered, "the counters must not drift");
        after.Extracted.Should().Be(counters.Extracted);
    }

    /// <summary>Creates a file job, uploads the CSV, maps it, starts it and runs it out.</summary>
    private static async Task<long> RunImportAsync(WebApplicationFactory<Program> factory)
    {
        var jobId = await CreateAndStartAsync(factory, withFile: true);

        // Each step is a separate lease in production; here they run in a row until the job
        // reports it has nothing left.
        for (var step = 0; step < 20; step++)
        {
            var result = await InScopeAsync(factory, async services =>
            {
                var job = await services.GetRequiredService<IImportJobRepository>().GetAsync(jobId, Ct);

                return await services.GetRequiredService<IImportPipeline>()
                    .RunStepAsync(jobId, job!.StepSequence, Ct);
            });

            if (!result.Value!.MoreWork) break;
        }

        return jobId;
    }

    private static async Task<long> CreateAndStartAsync(
        WebApplicationFactory<Program> factory, bool withFile = false)
    {
        return await InScopeAsync(factory, async services =>
        {
            var jobs = services.GetRequiredService<IImportJobService>();

            var created = await jobs.CreateAsync(
                new CreateImportJobSpec(
                    withFile ? ImportMode.File : ImportMode.Crawl,
                    withFile ? [] : ["https://shop.example.com/catalogue"],
                    [],
                    null,
                    [],
                    [],
                    ImportLimits.Default,
                    PublishPolicy.ManualReview,
                    FileRightsAttested: true),
                Admin,
                Ct);

            created.IsSuccess.Should().BeTrue(created.Error?.Message);
            var jobId = created.Value;

            if (withFile)
            {
                var files = services.GetRequiredService<IImportFileService>();

                var uploaded = await files.UploadAsync(
                    jobId, new MemoryStream(Encoding.UTF8.GetBytes(Csv)),
                    ImportFileFormat.Csv, Admin, Ct);

                uploaded.IsSuccess.Should().BeTrue(uploaded.Error?.Message);

                var mapped = await files.SetMappingAsync(jobId, Mapping, Admin, Ct);
                mapped.IsSuccess.Should().BeTrue(mapped.Error?.Message);

                var started = await jobs.StartAsync(jobId, Admin, Ct);
                started.IsSuccess.Should().BeTrue(started.Error?.Message);
            }

            return jobId;
        });
    }

    private static Task<IReadOnlyList<StagedRecordListItemDto>> RecordsForAsync(
        WebApplicationFactory<Program> factory, long jobId) =>
        InScopeAsync(factory, async services =>
        {
            var page = await services.GetRequiredService<ISourcingAdminQueries>()
                .ListRecordsAsync(new StagedRecordFilter(JobId: jobId, PageSize: 200), Ct);

            return page.Value!.Items;
        });

    private static Task<int> TotalRecordsAsync(WebApplicationFactory<Program> factory) =>
        InScopeAsync(factory, async services =>
        {
            var page = await services.GetRequiredService<ISourcingAdminQueries>()
                .ListRecordsAsync(new StagedRecordFilter(PageSize: 200), Ct);

            return page.Value!.Items.Count;
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
}
