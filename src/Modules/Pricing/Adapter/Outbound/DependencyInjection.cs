using EwaysVNext.Modules.Pricing.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Pricing.Adapter.Outbound.Persistence;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddPricingOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<ICurrencyRepository, CurrencyRepository>();
        services.AddScoped<IFxRateRepository, FxRateRepository>();
        services.AddScoped<IFxRateOverrideRepository, FxRateOverrideRepository>();
        services.AddScoped<IVariantPriceRepository, VariantPriceRepository>();
        services.AddScoped<IResolvedRateProjection, ResolvedRateProjection>();
        services.AddScoped<IPricingPolicyGateway, PricingPolicyGateway>();
        services.AddScoped<IPricingChangeNotifier, PricingChangeNotifier>();
        return services;
    }
}
