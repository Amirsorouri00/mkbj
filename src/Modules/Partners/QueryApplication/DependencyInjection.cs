using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Partners.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddPartnersQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<IPartnerAdminQueries, PartnerAdminQueries>();

        return services;
    }
}
