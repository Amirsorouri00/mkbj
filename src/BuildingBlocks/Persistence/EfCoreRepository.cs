using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Persistence;

public abstract class EfCoreRepository<TAggregate, TId>(DbContext context)
    where TAggregate : class
    where TId : notnull
{
    protected DbContext Context { get; } = context;
    protected DbSet<TAggregate> Set { get; } = context.Set<TAggregate>();

    public virtual Task<TAggregate?> GetByIdAsync(TId id, CancellationToken ct = default)
        => Set.FindAsync([id], ct).AsTask();

    public virtual async Task AddAsync(TAggregate aggregate, CancellationToken ct = default)
        => await Set.AddAsync(aggregate, ct);
}
