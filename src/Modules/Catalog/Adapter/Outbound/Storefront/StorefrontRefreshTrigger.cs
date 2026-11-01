using EwaysVNext.Domain.Common;
using EwaysVNext.Jobs;
using EwaysVNext.Modules.Catalog.Application.Storefront.Ports.Inbound;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;

public sealed class StorefrontRefreshTrigger(IJobQueue jobs, IClock clock) : IStorefrontRefreshTrigger
{
    /// <summary>
    /// Long enough for a burst of edits to collapse into one rebuild, short enough that an admin
    /// who saves a product and reloads the storefront sees it. The queue drops a second enqueue
    /// while one with this key is still waiting, so a thousand-row import costs one refresh.
    /// </summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);

    private const string DedupeKey = "storefront";

    public Task RequestRefreshAsync(CancellationToken ct = default)
        => jobs.EnqueueAsync(
            JobTypes.RefreshStorefrontViews,
            new RefreshStorefrontViewsPayload(),
            clock.UtcNow.Add(Delay),
            DedupeKey,
            ct);
}
