using AwesomeAssertions;
using EwaysVNext.Jobs;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;
using EwaysVNext.Modules.Catalog.Application.Storefront.Ports.Inbound;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// The storefront's materialized views are rebuilt by a job, not inline. These tests pin the two
/// numbers that make that bearable: the short delay that lets a burst of edits collapse into one
/// rebuild, and the dedupe key that makes the collapsing happen at all.
/// </summary>
public sealed class StorefrontRefreshTriggerTests
{
    private static readonly DateTimeOffset T0 = TestTime.Now;

    [Fact]
    public async Task RequestRefresh_Enqueues_Deduplicated_Job_Five_Seconds_Out()
    {
        var queue = Substitute.For<IJobQueue>();
        var clock = new FixedClock(T0);

        await new StorefrontRefreshTrigger(queue, clock).RequestRefreshAsync(
            TestContext.Current.CancellationToken);

        await queue.Received(1).EnqueueAsync(
            JobTypes.RefreshStorefrontViews,
            Arg.Any<RefreshStorefrontViewsPayload>(),
            T0.AddSeconds(5),
            "storefront",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Several_Requests_All_Use_The_Same_Dedupe_Key()
    {
        var queue = Substitute.For<IJobQueue>();
        var trigger = new StorefrontRefreshTrigger(queue, new FixedClock(T0));

        await trigger.RequestRefreshAsync(TestContext.Current.CancellationToken);
        await trigger.RequestRefreshAsync(TestContext.Current.CancellationToken);

        // Same key every time, which is what lets the queue coalesce an admin's bulk edit into
        // one rebuild instead of one per row.
        await queue.Received(2).EnqueueAsync(
            Arg.Any<string>(), Arg.Any<RefreshStorefrontViewsPayload>(),
            Arg.Any<DateTimeOffset?>(), "storefront", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_Delay_Is_Measured_From_The_Injected_Clock()
    {
        var queue = Substitute.For<IJobQueue>();
        var later = T0.AddDays(3);

        await new StorefrontRefreshTrigger(queue, new FixedClock(later))
            .RequestRefreshAsync(TestContext.Current.CancellationToken);

        await queue.Received(1).EnqueueAsync(
            Arg.Any<string>(), Arg.Any<RefreshStorefrontViewsPayload>(),
            later.AddSeconds(5), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
