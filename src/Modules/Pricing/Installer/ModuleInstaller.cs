using EwaysVNext.Database;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Pricing.Adapter.Outbound;
using EwaysVNext.Modules.Pricing.Application;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Services;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Pricing;

public static class ModuleInstaller
{
    /// <summary>
    /// Takes <see cref="IConfiguration"/> because pricing reads two settings that are
    /// deliberately not stored: the FX provider's address and how stale a rate may be. Both
    /// belong to the deployment rather than to the seller's data.
    /// </summary>
    public static IServiceCollection AddPricingModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPricingOutboundAdapters(configuration);
        services.AddScoped<ResolvedRateProjectionService>();
        services.AddScoped<FxBookLoader>();
        services.AddScoped<IPriceQuotePort, PriceQuoteService>();
        services.AddScoped<IFxSnapshotPort, FxSnapshotService>();
        services.AddScoped<IPricePublishingPort, PricePublishingService>();
        services.AddScoped<IPricingAdminService, PricingAdminService>();

        services
            .AddPersistenceInterceptors()
            .AddUnitOfWork<EwaysVNextDbContext>()
            .AddEvents(typeof(PricingErrors).Assembly);

        return services;
    }
}
