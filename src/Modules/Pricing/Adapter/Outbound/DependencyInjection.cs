using EwaysVNext.Modules.Pricing.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Pricing.Adapter.Outbound.Persistence;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;
using EwaysVNext.Jobs;
using EwaysVNext.Jobs.Implementation;
using EwaysVNext.Modules.Pricing.Adapter.Outbound.Fx;
using EwaysVNext.Modules.Pricing.Adapter.Outbound.Fx.Ecb;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddPricingOutboundAdapters(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<ICurrencyRepository, CurrencyRepository>();
        services.AddScoped<IFxRateRepository, FxRateRepository>();
        services.AddScoped<IFxRateOverrideRepository, FxRateOverrideRepository>();
        services.AddScoped<IVariantPriceRepository, VariantPriceRepository>();
        services.AddScoped<IResolvedRateProjection, ResolvedRateProjection>();
        services.AddScoped<IPricingPolicyGateway, PricingPolicyGateway>();
        services.AddScoped<IPricingChangeNotifier, PricingChangeNotifier>();
        services.AddScoped<ICatalogVariantGateway, CatalogVariantGateway>();
        services.AddScoped<IFxRateProvider>(
            provider => provider.GetRequiredService<EcbFxRateProvider>());
        services.AddScoped<IFxRefreshService, FxRefreshService>();

        // A typed client so the provider gets the pipeline's timeouts and logging rather than a
        // bare HttpClient it would have to configure itself.
        services.AddHttpClient<EcbFxRateProvider>(client =>
            client.BaseAddress = new Uri(
                configuration["Fx:Ecb:BaseUrl"] ?? "https://www.ecb.europa.eu/stats/eurofxref/"));

        services.AddJobHandler<FxRefreshJobHandler, FxRefreshPayload>();
        services.AddRecurringJob(
            JobTypes.FxRefresh,
            configuration.GetValue("Fx:RefreshInterval", TimeSpan.FromHours(6)));

        return services;
    }
}
