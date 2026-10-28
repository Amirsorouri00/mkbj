using System.Net;
using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Host;

/// <summary>
/// The one-shot commands the production image relies on. The refusal to seed in Production is the
/// important one: seed data is fictitious, and a partner invoice composed from fictitious SKUs is
/// exactly what the PRD forbids.
/// </summary>
public sealed class HostCommandsTests
{
    [Fact]
    public async Task Seed_Refuses_In_Production()
    {
        var seeder = new RecordingSeeder(10, []);
        await using var app = BuildApp("Production", seeder);

        var exitCode = await HostCommands.RunAsync(app, ["--seed"]);

        exitCode.Should().Be(2);
        seeder.Calls.Should().Be(0, "nothing may be seeded in Production, not even partially");
    }

    [Fact]
    public async Task Seed_Runs_Seeders_In_Order_Outside_Production()
    {
        var order = new List<int>();
        await using var app = BuildApp("Development", new RecordingSeeder(20, order), new RecordingSeeder(10, order));

        var exitCode = await HostCommands.RunAsync(app, ["--seed"]);

        exitCode.Should().Be(0);
        order.Should().Equal(10, 20);
    }

    [Fact]
    public async Task No_Command_Means_Serve()
    {
        await using var app = BuildApp("Development");

        (await HostCommands.RunAsync(app, [])).Should().BeNull();
    }

    [Fact]
    public void Worker_Flag_Selects_WorkerOnly()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Jobs:Mode"] = "InProcess";

        HostCommands.ApplyFlags(builder.Configuration, ["--worker"]);

        builder.Configuration["Jobs:Mode"].Should().Be("WorkerOnly");
    }

    [Fact]
    public async Task Probe_Fails_For_An_Unreachable_Url() =>
        (await Probe.RunAsync(["--probe", "http://127.0.0.1:1/health"])).Should().Be(1);

    [Fact]
    public async Task Probe_Without_Url_Is_A_Usage_Error() =>
        (await Probe.RunAsync(["--probe"])).Should().Be(64);

    private static WebApplication BuildApp(string environment, params IDataSeeder[] seeders)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        foreach (var seeder in seeders) builder.Services.AddSingleton(seeder);
        return builder.Build();
    }

    private sealed class RecordingSeeder(int order, List<int> log) : IDataSeeder
    {
        public int Order { get; } = order;
        public int Calls { get; private set; }

        public Task SeedAsync(CancellationToken ct = default)
        {
            Calls++;
            log.Add(Order);
            return Task.CompletedTask;
        }
    }
}

/// <summary>A worker process must not expose the API surface; only the health probes.</summary>
[Collection(PostgresSuite.Name)]
public sealed class WorkerOnlyHostTests(PostgresFixture fixture)
{
    [Fact]
    public async Task WorkerOnly_Host_Serves_Only_Health()
    {
        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["Jobs:Mode"] = "WorkerOnly",
        });
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/graphql", new StringContent("{}"))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Migrate_Is_Idempotent()
    {
        // Migrating twice must be a no-op the second time: the init container runs on every deploy.
        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = fixture.MigratorConnectionString,
        });

        await using var scope = factory.Services.CreateAsyncScope();
        await EwaysVNext.Database.DatabaseMigrator.MigrateAsync(scope.ServiceProvider);
        await EwaysVNext.Database.DatabaseMigrator.MigrateAsync(scope.ServiceProvider);
    }
}
