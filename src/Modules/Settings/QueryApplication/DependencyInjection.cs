using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Settings.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddSettingsQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<ISettingsAdminQueries, SettingsAdminQueries>();

        return services;
    }
}
