using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Storefront.Ports.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;

internal sealed class CatalogChangeNotifier(
    EwaysVNextDbContext db,
    IStorefrontRefreshTrigger refresh,
    ILogger<CatalogChangeNotifier> logger) : ICatalogChangeNotifier
{
    /// <summary>
    /// A sequence rather than a row, because every sellability change in the system has to bump
    /// it and a row would serialise them all behind one lock. <c>nextval</c> is transaction-free
    /// and never blocks; the gaps a rolled-back caller leaves do not matter, since the only
    /// question anyone asks is "is this the same number I saw before?".
    /// </summary>
    // Aliased "Value" because that is the column name SqlQueryRaw<long> binds a scalar from.
    private const string NextVersion =
        """SELECT nextval('catalog.sellability_version') AS "Value" """;

    /// <summary>
    /// A Postgres sequence that has never been called reports <c>last_value = 1</c> with
    /// <c>is_called = false</c>, and the first <c>nextval</c> returns that same 1. Reading
    /// <c>last_value</c> alone would therefore show no change across the first bump, and a cart
    /// created before the catalogue's very first edit would never notice it. Subtracting while
    /// uncalled makes the fresh sequence read 0, so every bump is visible.
    /// </summary>
    private const string LastVersion =
        """
        SELECT CASE WHEN is_called THEN last_value ELSE last_value - 1 END AS "Value"
        FROM catalog.sellability_version
        """;

    public async Task SellabilityChangedAsync(CancellationToken ct = default)
    {
        await SafelyAsync(
            async () =>
            {
                await db.Database.SqlQueryRaw<long>(NextVersion).SingleAsync(ct);
                await refresh.RequestRefreshAsync(ct);
            },
            "sellability",
            ct);
    }

    public Task DisplayChangedAsync(CancellationToken ct = default)
        // No version bump: a retitled product or a new image changes nothing about what a cart
        // holds, and bumping would invalidate every cart in the system over a typo.
        => SafelyAsync(() => refresh.RequestRefreshAsync(ct), "display", ct);

    public async Task<long> CurrentVersionAsync(CancellationToken ct = default)
        => await db.Database.SqlQueryRaw<long>(LastVersion).SingleAsync(ct);

    /// <summary>
    /// Notification failures are logged and swallowed. This runs after the caller's save has
    /// committed, so throwing here would report a failure for work that already succeeded — and
    /// the nightly refresh rebuilds the views regardless.
    /// </summary>
    private async Task SafelyAsync(Func<Task> action, string kind, CancellationToken ct)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not record a {Kind} catalogue change; the storefront views will catch up on the next scheduled refresh.",
                kind);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The request went away after the write committed. Nothing to report.
        }
    }
}
