using System.Diagnostics;
using EwaysVNext.Database;
using EwaysVNext.Database.Catalog.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;

/// <summary>
/// Rebuilds the storefront's materialized views.
/// </summary>
/// <remarks>
/// Adapter-level and public: the views are infrastructure, not domain, and the integration tests
/// resolve this directly to rebuild the shop between arrangements.
/// </remarks>
public interface IStorefrontViewRefresher
{
    Task RefreshAllAsync(CancellationToken ct = default);
}

/// <summary>
/// Refreshes each view in order through <c>catalog.refresh_storefront_view</c>.
/// </summary>
/// <remarks>
/// <para>
/// Order matters: each view reads the one before it, so refreshing out of order would project
/// yesterday's variants into today's products.
/// </para>
/// <para>
/// One command per view, not one transaction over all four. A concurrent refresh cannot run
/// inside a transaction block, and holding all four to one instant is not worth blocking the shop
/// for — a visitor seeing a product one refresh ahead of its facets is a display artefact that
/// lasts seconds.
/// </para>
/// </remarks>
public sealed class StorefrontViewRefresher(
    EwaysVNextDbContext db, ILogger<StorefrontViewRefresher> logger) : IStorefrontViewRefresher
{
    /// <summary>
    /// A full rebuild of a large catalogue is minutes, not seconds, and the default 30 s would
    /// abandon it halfway through.
    /// </summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(300);

    /// <summary>
    /// <c>object_not_in_prerequisite_state</c> — the view has never been populated, so a
    /// concurrent refresh is not yet legal. Only possible if a view was created
    /// <c>WITH NO DATA</c>; the one blocking refresh that follows makes every later one concurrent.
    /// </summary>
    private const string NotYetPopulated = "55000";

    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        var previousTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(CommandTimeout);

        try
        {
            foreach (var view in StorefrontViewSql.RefreshOrder) await RefreshAsync(view, ct);
        }
        finally
        {
            db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    private async Task RefreshAsync(string view, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            await ExecuteAsync(view, concurrently: true, ct);
        }
        catch (PostgresException exception) when (exception.SqlState == NotYetPopulated)
        {
            // Blocking, and only once: this takes ACCESS EXCLUSIVE, but a view with no data is
            // serving nothing anyway.
            logger.LogWarning(
                "{View} has never been populated; refreshing it without CONCURRENTLY.", view);

            await ExecuteAsync(view, concurrently: false, ct);
        }

        logger.LogInformation(
            "Refreshed {View} in {Elapsed}.", view, Stopwatch.GetElapsedTime(started));
    }

    private Task<int> ExecuteAsync(string view, bool concurrently, CancellationToken ct) =>
        // The name is a constant from RefreshOrder, never input — and the function checks it
        // again on its own side, because it runs as the schema's owner.
        db.Database.ExecuteSqlRawAsync(
            "SELECT catalog.refresh_storefront_view({0}, {1})", [view, concurrently], ct);
}
