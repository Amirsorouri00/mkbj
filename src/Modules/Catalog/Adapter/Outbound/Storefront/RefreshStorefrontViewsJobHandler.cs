using EwaysVNext.Jobs;
using EwaysVNext.Modules.Catalog.Application.Storefront.Ports.Inbound;
using Npgsql;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;

/// <summary>
/// Rebuilds the storefront views, on the 5-second debounce after a catalogue change and on a
/// 24-hour backstop.
/// </summary>
/// <remarks>
/// Idempotent, as every handler must be: refreshing a view twice produces the same view.
/// </remarks>
public sealed class RefreshStorefrontViewsJobHandler(IStorefrontViewRefresher refresher)
    : IJobHandler<RefreshStorefrontViewsPayload>
{
    /// <summary>
    /// Half a minute. A refresh fails transiently when the database is restarting or a connection
    /// is cut; the storefront is stale meanwhile, so waiting longer costs visibility.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    public string Type => JobTypes.RefreshStorefrontViews;

    public async Task<JobOutcome> HandleAsync(
        RefreshStorefrontViewsPayload payload, JobContext context, CancellationToken ct = default)
    {
        try
        {
            await refresher.RefreshAllAsync(ct);
            return JobOutcome.Completed;
        }
        catch (NpgsqlException exception) when (exception.IsTransient)
        {
            // RetryLater, never Failed: a database that blinked is not a job that is wrong, and
            // giving up would leave the shop showing a catalogue nobody can correct.
            return JobOutcome.RetryLater(RetryDelay, exception.GetType().Name);
        }
    }
}
