namespace EwaysVNext.Events.Application;

public interface IDomainEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IDomainEvent;
}
