using EwaysVNext.Events.Application;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Events.Implementation;

public sealed class DomainEventBus(IServiceProvider serviceProvider) : IDomainEventBus
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IDomainEvent
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(@event.GetType());
        var handlers = serviceProvider.GetServices(handlerType);

        var tasks = handlers
            .Where(handler => handler is not null)
            .Select(handler => (Task)handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!
                .Invoke(handler, [@event, ct])!);

        await Task.WhenAll(tasks);
    }
}
