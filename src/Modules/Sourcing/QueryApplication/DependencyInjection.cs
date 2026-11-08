using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sourcing.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddSourcingQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<ISourcingAdminQueries, SourcingAdminQueries>();

        return services;
    }
}
