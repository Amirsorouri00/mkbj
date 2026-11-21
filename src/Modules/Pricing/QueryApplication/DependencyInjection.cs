using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Pricing.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddPricingQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<ICurrencyQueries, CurrencyQueries>();
        services.AddScoped<IPricingAdminQueries, PricingAdminQueries>();
        return services;
    }
}
