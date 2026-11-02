using AwesomeAssertions;
using EwaysVNext.Jobs;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;
using EwaysVNext.Modules.Catalog.Application.Storefront.Ports.Inbound;
using Npgsql;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// The storefront refresh job's two outcomes.
/// </summary>
/// <remarks>
/// A refresh that fails transiently must come back, not be marked failed: the shop would
/// otherwise keep showing a catalogue nobody can correct until the 24-hour backstop fires.
/// </remarks>
public sealed class RefreshStorefrontViewsJobHandlerTests
{
    private static readonly JobContext Context = new(1, 1, _ => Task.CompletedTask);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handler_Refreshes_Then_Completes()
    {
        var refresher = new RecordingRefresher();
        var handler = new RefreshStorefrontViewsJobHandler(refresher);

        var outcome = await handler.HandleAsync(new RefreshStorefrontViewsPayload(), Context, Ct);

        outcome.Should().Be(JobOutcome.Completed);
        refresher.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Transient_Npgsql_Error_Retries_Later()
    {
        // 40001 is serialization_failure, which Npgsql itself reports as transient — the handler
        // asks the exception rather than keeping its own list of codes to drift out of date.
        var handler = new RefreshStorefrontViewsJobHandler(
            new ThrowingRefresher(new PostgresException("conflict", "ERROR", "ERROR", "40001")));

        var outcome = await handler.HandleAsync(new RefreshStorefrontViewsPayload(), Context, Ct);

        outcome.Should().BeOfType<JobOutcome.RetryLaterOutcome>()
            .Which.Delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_Permanent_Error_Is_Not_Swallowed()
    {
        // 42P01 is undefined_table: the views are gone, which no amount of retrying fixes and
        // nobody should learn about from a quietly stale storefront.
        var handler = new RefreshStorefrontViewsJobHandler(
            new ThrowingRefresher(new PostgresException("missing", "ERROR", "ERROR", "42P01")));

        var act = async () =>
            await handler.HandleAsync(new RefreshStorefrontViewsPayload(), Context, Ct);

        await act.Should().ThrowAsync<PostgresException>();
    }

    private sealed class RecordingRefresher : IStorefrontViewRefresher
    {
        public int Calls { get; private set; }

        public Task RefreshAllAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingRefresher(Exception exception) : IStorefrontViewRefresher
    {
        public Task RefreshAllAsync(CancellationToken ct = default) => throw exception;
    }
}
