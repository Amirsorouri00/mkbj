using EwaysVNext.Events.Application;

namespace EwaysVNext.Domain.Common;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

public abstract class Entity<TId> : IHasDomainEvents where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    /// <summary>
    /// Optimistic-concurrency token, mapped to Postgres's system column <c>xmin</c> (ADR-0017):
    /// the database assigns it, nothing here ever writes it, and no migration creates a column for it.
    /// <para>
    /// It is a real token only on aggregate ROOTS. Every entity inherits the property, but a child
    /// or owned row can only be written through its root, so the root's token is the one that
    /// matters — that is the correct DDD granularity, not an oversight. Do not read the presence of
    /// this property on a child as a promise of child-level concurrency checking.
    /// </para>
    /// </summary>
    public uint RowVersion { get; protected set; }

    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent @event) => _domainEvents.Add(@event);
    public void ClearDomainEvents() => _domainEvents.Clear();

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return Id.Equals(other.Id);
    }

    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(Entity<TId>? a, Entity<TId>? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(Entity<TId>? a, Entity<TId>? b) => !(a == b);
}
