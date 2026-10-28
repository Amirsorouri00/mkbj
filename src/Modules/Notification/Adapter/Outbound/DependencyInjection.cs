using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Modules.Notification.Adapter.Outbound.Sms;
using EwaysVNext.Modules.Notification.Adapter.Outbound.Sms.PayamSms;
using EwaysVNext.Modules.Notification.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Notification.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationOutboundAdapters(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SmsOptions>(config.GetSection("Sms"));
        services.AddScoped<ISmsProviderResolver, SmsProviderResolver>();

        services.AddPayamSmsProvider(config);

        return services;
    }
}
