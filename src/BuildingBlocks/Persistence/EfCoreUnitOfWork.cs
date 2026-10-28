using EwaysVNext.Domain.Common;
using EwaysVNext.Events.Application;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Persistence;

public sealed class EfCoreUnitOfWork<TDbContext>(TDbContext db, IDomainEventBus eventBus) : IUnitOfWork
    where TDbContext : DbContext
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var pending = CapturePendingEvents();
        var result = await db.SaveChangesAsync(ct);
        await DispatchAsync(pending, ct);
        return result;
    }

    public int SaveChanges()
    {
        var pending = CapturePendingEvents();
        var result = db.SaveChanges();
        DispatchAsync(pending, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    private List<IDomainEvent> CapturePendingEvents()
    {
        var events = new List<IDomainEvent>();
        foreach (var aggregate in db.ChangeTracker.Entries<IAggregateRoot>().Select(e => e.Entity).ToList())
        {
            if (aggregate.DomainEvents.Count == 0) continue;
            events.AddRange(aggregate.DomainEvents);
            aggregate.ClearDomainEvents();
        }
        return events;
    }

    private async Task DispatchAsync(List<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var @event in events)
            await eventBus.PublishAsync(@event, ct);
    }
}
