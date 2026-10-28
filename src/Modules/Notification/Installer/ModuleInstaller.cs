using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Modules.Notification.Adapter.Inbound.Rest;
using EwaysVNext.Modules.Notification.Adapter.Outbound;
using EwaysVNext.Modules.Notification.Application;

namespace EwaysVNext.Modules.Notification;

public static class ModuleInstaller
{
    public static IServiceCollection AddNotificationModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddNotificationApplication();
        services.AddNotificationOutboundAdapters(config);
        services.AddNotificationRestAdapter();

        return services;
    }
}
