using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Modules.Notification.Adapter.Inbound.Rest.Endpoints;

namespace EwaysVNext.Modules.Notification.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationRestAdapter(this IServiceCollection services)
    {
        services.AddEndpoints(typeof(NotificationEndpoints).Assembly);
        return services;
    }
}
