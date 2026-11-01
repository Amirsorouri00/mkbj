using EwaysVNext.Modules.Pricing.Adapter.Outbound.Persistence;
using EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddPricingOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<ICurrencyRepository, CurrencyRepository>();
        services.AddScoped<IFxRateRepository, FxRateRepository>();
        services.AddScoped<IFxRateOverrideRepository, FxRateOverrideRepository>();
        return services;
    }
}
