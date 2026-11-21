using Mkbj.Modules.Settings.Adapter.Outbound.Persistence;
using Mkbj.Modules.Settings.Application.Settings.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Settings.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddSettingsOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<ISettingsRepository, SettingsRepository>();

        return services;
    }
}
