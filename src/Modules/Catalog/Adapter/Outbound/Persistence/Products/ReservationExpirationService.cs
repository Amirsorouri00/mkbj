using EwaysVNext.Domain.Common;
using EwaysVNext.Persistence;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Products;

internal sealed class ReservationExpirationService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReservationExpirationService> logger,
    IClock clock) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan CommittedRetention = TimeSpan.FromDays(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Reservation TTL reaper started (interval: {Interval})", Interval);
        using var timer = new PeriodicTimer(Interval);

        try
        {
            do
            {
                try { await ReapOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Reservation reaper cycle failed; will retry next tick");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Reservation TTL reaper stopping");
        }
    }

    private async Task ReapOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var now = clock.UtcNow;
        var products = await repo.FindWithReclaimableReservationsAsync(now, CommittedRetention, ct);
        if (products.Count == 0) return;

        var releasedCount = 0;
        var prunedCount = 0;

        foreach (var product in products)
        {
            var released = product.ReleaseExpiredReservations(now);
            releasedCount += released.Count;
            foreach (var id in released)
                logger.LogInformation("Released expired reservation {ReservationId} on product {ProductId}",
                    id, product.Id);

            prunedCount += product.PruneCommittedReservations(now, CommittedRetention).Count;
        }

        await uow.SaveChangesAsync(ct);

        logger.LogInformation(
            "Reservation reaper: released {Released} expired hold(s), pruned {Pruned} committed row(s) past {Retention} across {ProductCount} product(s)",
            releasedCount, prunedCount, CommittedRetention, products.Count);
    }
}
