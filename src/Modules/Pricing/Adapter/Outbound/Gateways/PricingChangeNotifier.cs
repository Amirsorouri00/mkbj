using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Storefront.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Services;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound.Gateways;

internal sealed class PricingChangeNotifier(
    EwaysVNextDbContext db,
    ResolvedRateProjectionService projection,
    IStorefrontRefreshTrigger refresh,
    IClock clock,
    ILogger<PricingChangeNotifier> logger) : IPricingChangeNotifier
{
    // Aliased "Value" because that is the column name SqlQueryRaw<long> binds a scalar from.
    private const string NextVersion =
        """SELECT nextval('pricing.price_version') AS "Value" """;

    /// <summary>
    /// A never-called sequence reports <c>last_value = 1</c> with <c>is_called = false</c>, and
    /// the first <c>nextval</c> returns that same 1 — so reading <c>last_value</c> alone would
    /// show no change across the first bump. Subtracting while uncalled makes a fresh sequence
    /// read 0, so every bump is visible.
    /// </summary>
    private const string LastVersion =
        """
        SELECT CASE WHEN is_called THEN last_value ELSE last_value - 1 END AS "Value"
        FROM pricing.price_version
        """;

    public Task PricesChangedAsync(CancellationToken ct = default) =>
        SafelyAsync(
            async () =>
            {
                await db.Database.SqlQueryRaw<long>(NextVersion).SingleAsync(ct);
                await refresh.RequestRefreshAsync(ct);
            },
            "price",
            ct);

    public Task FxChangedAsync(CancellationToken ct = default) =>
        SafelyAsync(
            async () =>
            {
                // The rebuild comes first. Announcing before it would send every reader to the
                // rates that have just been superseded, which is the one window this whole
                // projection exists to avoid.
                await projection.RebuildAsync(clock.UtcNow, ct);
                await db.Database.SqlQueryRaw<long>(NextVersion).SingleAsync(ct);
                await refresh.RequestRefreshAsync(ct);
            },
            "FX",
            ct);

    public async Task<long> CurrentVersionAsync(CancellationToken ct = default) =>
        await db.Database.SqlQueryRaw<long>(LastVersion).SingleAsync(ct);

    /// <summary>
    /// Failures are logged and swallowed. This runs after the caller's save has committed, so
    /// throwing would report a failure for work that already succeeded — and the nightly refresh
    /// rebuilds everything regardless.
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
                "Could not record a {Kind} change; the derived tables will catch up on the next scheduled refresh.",
                kind);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The request went away after the write committed. Nothing to report.
        }
    }
}
