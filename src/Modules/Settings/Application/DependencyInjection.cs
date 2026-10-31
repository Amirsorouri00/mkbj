using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;
using EwaysVNext.Modules.Settings.Application.Settings.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Settings.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddSettingsApplication(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<FxAgeOptions>().Bind(configuration.GetSection(FxAgeOptions.SectionName));
        services.AddScoped<ISettingsPort, SettingsPortService>();
        services.AddScoped<ISettingsAdminService, SettingsAdminService>();

        return services;
    }
}
