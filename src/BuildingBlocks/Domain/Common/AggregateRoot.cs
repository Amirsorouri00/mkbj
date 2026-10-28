namespace EwaysVNext.Domain.Common;

public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot where TId : notnull
{
}
