using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Settings.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddSettingsQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<ISettingsAdminQueries, SettingsAdminQueries>();

        return services;
    }
}
