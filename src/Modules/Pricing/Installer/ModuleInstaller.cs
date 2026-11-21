using Mkbj.Database;
using Mkbj.DependencyInjection;
using Mkbj.Modules.Pricing.Adapter.Inbound.GraphQL;
using Mkbj.Modules.Pricing.Adapter.Inbound.Rest;
using Mkbj.Modules.Pricing.Adapter.Outbound;
using Mkbj.Modules.Pricing.QueryApplication;
using Mkbj.Modules.Pricing.Application;
using Mkbj.Modules.Pricing.Application.Fx.Ports.Inbound;
using Mkbj.Modules.Pricing.Application.Fx.Services;
using Mkbj.Modules.Pricing.Application.Prices.Ports.Inbound;
using Mkbj.Modules.Pricing.Application.Prices.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Pricing;

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
        services.AddPricingQueryApplication();
        services.AddPricingRestAdapter();
        services.AddPricingGraphQL();
        services.AddScoped<ResolvedRateProjectionService>();
        services.AddScoped<FxBookLoader>();
        services.AddScoped<IPriceQuotePort, PriceQuoteService>();
        services.AddScoped<IFxSnapshotPort, FxSnapshotService>();
        services.AddScoped<IPricePublishingPort, PricePublishingService>();
        services.AddScoped<IPricingAdminService, PricingAdminService>();

        services
            .AddPersistenceInterceptors()
            .AddUnitOfWork<MkbjDbContext>()
            .AddEvents(typeof(PricingErrors).Assembly);

        return services;
    }
}
