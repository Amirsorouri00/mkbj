namespace EwaysVNext.Events.Application;

public interface IDomainEvent : IEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredOn { get; }
}
