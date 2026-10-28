using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Jobs;
using EwaysVNext.Jobs.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Platform;

/// <summary>
/// The queue's concurrency contract, against a real Postgres. These are the properties the worker
/// loop depends on and that no unit test can establish: a job is claimed by exactly one worker, a
/// dead worker's job is reclaimed, and a worker that lost its lease cannot finish the job someone
/// else is now running.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class JobStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Enqueue_Inserts_A_Queued_Job()
    {
        await using var scope = NewScope();
        var type = NewType();

        await Queue(scope).EnqueueAsync(type, new { productId = 7L }, T0);

        var claimed = await Store(scope).ClaimAsync("w1", 10, Lease, T0);
        var job = claimed.Should().ContainSingle(j => j.Type == type).Subject;

        job.Attempts.Should().Be(1, "claiming counts as an attempt");
        job.Payload.Should().Contain("\"productId\": 7");
    }

    [Fact]
    public async Task Same_DedupeKey_While_Queued_Coalesces()
    {
        await using var scope = NewScope();
        var type = NewType();
        var key = $"dedupe-{Guid.NewGuid():N}";

        await Queue(scope).EnqueueAsync(type, EmptyPayload.Instance, T0.AddSeconds(5), key);
        await Queue(scope).EnqueueAsync(type, EmptyPayload.Instance, T0.AddSeconds(60), key);

        // The second enqueue is dropped, so the earlier run time wins: a refresh already due must
        // not be pushed back by a later request for the same work.
        var tooEarly = await Store(scope).ClaimAsync("w1", 10, Lease, T0);
        tooEarly.Should().NotContain(j => j.Type == type);

        var claimed = await Store(scope).ClaimAsync("w1", 10, Lease, T0.AddSeconds(5));
        claimed.Should().ContainSingle(j => j.Type == type);
    }

    [Fact]
    public async Task Same_DedupeKey_After_Claim_Inserts_A_New_Job()
    {
        await using var scope = NewScope();
        var type = NewType();
        var key = $"dedupe-{Guid.NewGuid():N}";

        await Queue(scope).EnqueueAsync(type, EmptyPayload.Instance, T0, key);
        await Store(scope).ClaimAsync("w1", 10, Lease, T0);

        // Work requested while a run is in flight gets its own run; the running job may already
        // have read the state the new request is about.
        await Queue(scope).EnqueueAsync(type, EmptyPayload.Instance, T0, key);

        var claimed = await Store(scope).ClaimAsync("w2", 10, Lease, T0);
        claimed.Should().ContainSingle(j => j.Type == type);
    }

    [Fact]
    public async Task Future_RunAt_Is_Not_Claimed()
    {
        await using var scope = NewScope();
        var type = NewType();

        await Queue(scope).EnqueueAsync(type, EmptyPayload.Instance, T0.AddMinutes(10));

        (await Store(scope).ClaimAsync("w1", 10, Lease, T0))
            .Should().NotContain(j => j.Type == type);
    }

    [Fact]
    public async Task Concurrent_Claims_Never_Share_A_Job()
    {
        var type = NewType();

        await using (var seed = NewScope())
        {
            for (var i = 0; i < 50; i++)
                await Queue(seed).EnqueueAsync(type, new { index = i }, T0);
        }

        // Five workers, each in its own scope and connection, draining until empty.
        var claimedIds = new System.Collections.Concurrent.ConcurrentBag<long>();

        await Task.WhenAll(Enumerable.Range(0, 5).Select(async worker =>
        {
            await using var scope = WorkerScope();
            var store = Store(scope);

            while (true)
            {
                var batch = await store.ClaimAsync($"w{worker}", 4, Lease, T0);
                var mine = batch.Where(j => j.Type == type).ToList();
                if (batch.Count == 0) break;
                foreach (var job in mine) claimedIds.Add(job.Id);
            }
        }));

        claimedIds.Should().HaveCount(50).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Expired_Lease_Is_Reclaimed()
    {
        await using var scope = NewScope();
        var type = NewType();
        await Queue(scope).EnqueueAsync(type, EmptyPayload.Instance, T0);

        var first = (await Store(scope).ClaimAsync("w1", 10, Lease, T0)).Single(j => j.Type == type);

        (await Store(scope).ClaimAsync("w2", 10, Lease, T0.AddMinutes(4)))
            .Should().NotContain(j => j.Type == type, "the lease has not expired yet");

        var reclaimed = (await Store(scope).ClaimAsync("w2", 10, Lease, T0.AddMinutes(5).AddSeconds(1)))
            .Single(j => j.Type == type);

        reclaimed.Id.Should().Be(first.Id);
        reclaimed.Attempts.Should().Be(2, "a reclaim is another attempt");
    }

    [Fact]
    public async Task Heartbeat_After_Lease_Lost_Returns_False()
    {
        await using var scope = NewScope();
        var type = NewType();
        await Queue(scope).EnqueueAsync(type, EmptyPayload.Instance, T0);

        var job = (await Store(scope).ClaimAsync("w1", 10, Lease, T0)).Single(j => j.Type == type);
        await Store(scope).ClaimAsync("w2", 10, Lease, T0.AddMinutes(6));

        (await Store(scope).HeartbeatAsync(job.Id, "w1", Lease, T0.AddMinutes(6)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Complete_By_A_Worker_That_Lost_The_Lease_Is_Ignored()
    {
        await using var scope = NewScope();
        var type = NewType();
        await Queue(scope).EnqueueAsync(type, EmptyPayload.Instance, T0);

        var job = (await Store(scope).ClaimAsync("w1", 10, Lease, T0)).Single(j => j.Type == type);
        await Store(scope).ClaimAsync("w2", 10, Lease, T0.AddMinutes(6));

        // w1 finishing here would mark the job done while w2 is still running it.
        await Store(scope).CompleteAsync(job.Id, "w1", T0.AddMinutes(7));

        (await Store(scope).HeartbeatAsync(job.Id, "w2", Lease, T0.AddMinutes(7)))
            .Should().BeTrue("the job must still belong to w2");
    }

    [Fact]
    public async Task Recurring_Tick_Yields_A_Type_Once_Per_Interval()
    {
        await using var scope = NewScope();
        var type = NewType();

        await Store(scope).RegisterRecurringAsync(type, TimeSpan.FromMinutes(10), T0);

        (await Store(scope).ClaimDueRecurringAsync(T0)).Should().NotContain(type);
        (await Store(scope).ClaimDueRecurringAsync(T0.AddMinutes(10))).Should().Contain(type);
        (await Store(scope).ClaimDueRecurringAsync(T0.AddMinutes(10)))
            .Should().NotContain(type, "the tick advances next_run_at as it reads");
    }

    /// <summary>
    /// A clean queue per test. Without it the claim's LIMIT is filled by rows an earlier test left
    /// behind, and a test asserting on its own job silently measures someone else's.
    /// </summary>
    private AsyncServiceScope NewScope()
    {
        var scope = fixture.CreateFactory().Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        db.Database.ExecuteSqlRaw("DELETE FROM jobs.job; DELETE FROM jobs.recurring;");
        return scope;
    }

    /// <summary>A scope that does NOT reset — for the workers in the concurrency test.</summary>
    private AsyncServiceScope WorkerScope() => fixture.CreateFactory().Services.CreateAsyncScope();
    private static IJobQueue Queue(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IJobQueue>();
    private static IJobStore Store(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IJobStore>();
    private static string NewType() => $"Test-{Guid.NewGuid():N}";
}
