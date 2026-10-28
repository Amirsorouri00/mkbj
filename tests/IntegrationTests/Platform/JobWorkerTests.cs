using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Database;
using EwaysVNext.Jobs;
using EwaysVNext.Jobs.Implementation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EwaysVNext.IntegrationTests.Platform;

/// <summary>
/// What the worker does with each handler outcome, and what it does when a handler misbehaves.
/// The retry and failure paths matter most: a job that silently stops retrying is work the system
/// has quietly dropped.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class JobWorkerTests(PostgresFixture fixture)
{
    public const string TestJobType = "TestJob";
    public const string TickJobType = "TestTick";

    [Fact]
    public async Task Completed_Marks_The_Job_Completed()
    {
        await using var host = NewHost();
        TestHandler.Outcome = _ => JobOutcome.Completed;

        var id = await EnqueueAsync(host);
        await host.Worker.RunOnceAsync();

        var job = await ReadAsync(host, id);
        job.Status.Should().Be(JobStatus.Completed);
        job.LockedBy.Should().BeNull();
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RetryLater_Requeues_With_Delay_And_Reason()
    {
        await using var host = NewHost();
        TestHandler.Outcome = _ => JobOutcome.RetryLater(TimeSpan.FromSeconds(30), "busy");

        var id = await EnqueueAsync(host);
        var before = DateTimeOffset.UtcNow;
        await host.Worker.RunOnceAsync();

        var job = await ReadAsync(host, id);
        job.Status.Should().Be(JobStatus.Queued);
        job.LastError.Should().Be("busy");
        job.RunAt.Should().BeCloseTo(before.AddSeconds(30), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Exception_Requeues_With_Backoff()
    {
        await using var host = NewHost();
        TestHandler.Outcome = _ => throw new InvalidOperationException("handler exploded");

        var id = await EnqueueAsync(host);
        var before = DateTimeOffset.UtcNow;
        await host.Worker.RunOnceAsync();

        var job = await ReadAsync(host, id);
        job.Status.Should().Be(JobStatus.Queued);
        job.LastError.Should().Contain("handler exploded");
        job.RunAt.Should().BeCloseTo(before.AddSeconds(5), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task MaxAttempts_Reached_Marks_Failed()
    {
        await using var host = NewHost(new Dictionary<string, string?> { ["Jobs:MaxAttempts"] = "2" });
        TestHandler.Outcome = _ => throw new InvalidOperationException("still broken");

        var id = await EnqueueAsync(host);

        await host.Worker.RunOnceAsync();            // attempt 1 → requeued
        await MakeDueAsync(host, id);
        await host.Worker.RunOnceAsync();            // attempt 2 → budget spent

        (await ReadAsync(host, id)).Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task Failed_Outcome_Marks_Failed_Immediately()
    {
        await using var host = NewHost();
        TestHandler.Outcome = _ => JobOutcome.Failed("unrecoverable");

        var id = await EnqueueAsync(host);
        await host.Worker.RunOnceAsync();

        var job = await ReadAsync(host, id);
        job.Status.Should().Be(JobStatus.Failed);
        job.LastError.Should().Be("unrecoverable");
    }

    [Fact]
    public async Task Unknown_Type_Is_Failed()
    {
        await using var host = NewHost();

        var id = await EnqueueAsync(host, type: "nope");
        await host.Worker.RunOnceAsync();

        var job = await ReadAsync(host, id);
        job.Status.Should().Be(JobStatus.Failed);
        job.LastError.Should().Be("no handler registered for job type 'nope'");
    }

    [Fact]
    public async Task Retry_Into_A_Queued_Duplicate_Is_Coalesced()
    {
        await using var host = NewHost();
        TestHandler.Outcome = _ => JobOutcome.RetryLater(TimeSpan.FromSeconds(30), "busy");
        var key = $"dedupe-{Guid.NewGuid():N}";

        var running = await EnqueueAsync(host, dedupeKey: key);
        await ClaimOnlyAsync(host);                       // the job is now Running, key free
        await EnqueueAsync(host, dedupeKey: key);         // a newer job takes the key

        await host.Worker.RunOnceAsync();                 // claims the new one; the old is stuck Running

        // Requeuing `running` would collide with the queued duplicate.
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        await store.RetryAsync(running, WorkerId, DateTimeOffset.UtcNow, "busy");

        var job = await ReadAsync(host, running);
        job.Status.Should().Be(JobStatus.Completed);
        job.LastError.Should().Be("coalesced into queued duplicate");
    }

    [Fact]
    public async Task Handler_Sees_The_Job_Correlation_Id()
    {
        await using var host = NewHost();
        string? seen = null;
        TestHandler.Outcome = context => { seen = context; return JobOutcome.Completed; };

        var id = await EnqueueAsync(host);
        await host.Worker.RunOnceAsync();

        seen.Should().Be($"job-{id}");
    }

    [Fact]
    public async Task Recurring_Job_Fires_Once_Even_With_Two_Workers()
    {
        await using var host = NewHost();
        var now = DateTimeOffset.UtcNow;

        await using (var seed = host.Factory.Services.CreateAsyncScope())
        {
            await seed.ServiceProvider.GetRequiredService<IJobStore>()
                .RegisterRecurringAsync(TickJobType, TimeSpan.FromMinutes(1), now);
        }

        // Two workers ticking at the same instant, each on its own connection as real workers are:
        // the guarded UPDATE must hand the tick to exactly one of them.
        async Task<IReadOnlyList<string>> TickAsync()
        {
            await using var scope = host.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IJobStore>()
                .ClaimDueRecurringAsync(now.AddMinutes(1));
        }

        var due = await Task.WhenAll(TickAsync(), TickAsync());

        due.SelectMany(d => d).Count(t => t == TickJobType).Should().Be(1);
    }

    [Fact]
    public async Task Disabled_Mode_Processes_Nothing()
    {
        await using var host = NewHost();                 // Jobs:Mode = Disabled
        TestHandler.Outcome = _ => JobOutcome.Completed;
        var id = await EnqueueAsync(host);

        // Start the loop as the host would. It must return immediately rather than drain the queue.
        await host.Worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));
        await host.Worker.StopAsync(CancellationToken.None);

        (await ReadAsync(host, id)).Status.Should().Be(JobStatus.Queued);
    }

    private const string WorkerId = "test-worker";

    private TestHost NewHost(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jobs:Mode"] = "Disabled",      // the loop stays off; tests drive RunOnceAsync
            ["Jobs:WorkerId"] = WorkerId,
            ["Jobs:BaseRetryDelay"] = "00:00:05",
        };
        if (overrides is not null)
            foreach (var (key, value) in overrides) settings[key] = value;

        var factory = fixture.CreateFactory(settings, services =>
        {
            services.AddJobHandler<TestHandler, TestPayload>();
            // Jobs:Mode is Disabled, so AddJobs registered no worker; this one is driven by hand.
            services.AddSingleton<JobWorker>();
        });

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
                .Database.ExecuteSqlRaw("DELETE FROM jobs.job; DELETE FROM jobs.recurring;");
        }

        return new TestHost(factory);
    }

    private static async Task<long> EnqueueAsync(
        TestHost host, string type = TestJobType, string? dedupeKey = null)
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IJobQueue>()
            .EnqueueAsync(type, new TestPayload("x"), DateTimeOffset.UtcNow, dedupeKey);

        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        return await db.Jobs.Where(j => j.Type == type)
            .OrderByDescending(j => j.Id).Select(j => j.Id).FirstAsync();
    }

    private static async Task ClaimOnlyAsync(TestHost host)
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IJobStore>()
            .ClaimAsync(WorkerId, 10, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
    }

    private static async Task MakeDueAsync(TestHost host, long id)
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
            .Database.ExecuteSqlRawAsync($"UPDATE jobs.job SET run_at = now() - interval '1 hour' WHERE id = {id};");
    }

    private static async Task<JobRecord> ReadAsync(TestHost host, long id)
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
            .Jobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    private sealed class TestHost(WebApplicationFactory<Program> factory) : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public JobWorker Worker => Factory.Services.GetRequiredService<JobWorker>();

        public ValueTask DisposeAsync()
        {
            TestHandler.Outcome = null;
            Factory.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    public sealed record TestPayload(string Value);

    public sealed class TestHandler(ICorrelationContext correlation) : IJobHandler<TestPayload>
    {
        /// <summary>Set per test; receives the correlation id the worker established.</summary>
        public static Func<string, JobOutcome>? Outcome { get; set; }

        public string Type => TestJobType;

        public Task<JobOutcome> HandleAsync(TestPayload payload, JobContext context, CancellationToken ct = default) =>
            Task.FromResult(Outcome?.Invoke(correlation.CorrelationId) ?? JobOutcome.Completed);
    }
}
