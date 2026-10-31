using EwaysVNext.Modules.Settings.Adapter.Outbound.Persistence;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Settings.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddSettingsOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<ISettingsRepository, SettingsRepository>();

        return services;
    }
}
