using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Modules.Notification.Application.Ports.Inbound;
using EwaysVNext.Modules.Notification.Application.Service;

namespace EwaysVNext.Modules.Notification.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services)
    {
        services.AddScoped<INotificationService, NotificationService>();
        return services;
    }
}
