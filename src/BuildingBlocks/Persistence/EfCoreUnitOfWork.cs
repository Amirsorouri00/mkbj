using System.Data.Common;
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

    public async Task<SaveOutcome> TrySaveChangesAsync(CancellationToken ct = default)
    {
        var pending = CapturePendingEvents();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SaveOutcome.ConcurrencyConflict;
        }
        catch (DbUpdateException error) when (error.InnerException is DbException { SqlState: "23505" })
        {
            // The tracked graph is poisoned after a failed insert: a caller that reloads and
            // retries would otherwise re-send the same rejected rows.
            db.ChangeTracker.Clear();
            return SaveOutcome.UniqueViolation;
        }

        // Events are dispatched only after the write committed, exactly as SaveChangesAsync does.
        await DispatchAsync(pending, ct);
        return SaveOutcome.Saved;
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
