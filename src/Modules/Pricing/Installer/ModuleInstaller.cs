using EwaysVNext.Database;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Pricing.Adapter.Outbound;
using EwaysVNext.Modules.Pricing.Application;
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

        services.AddPricingOutboundAdapters();

        services
            .AddPersistenceInterceptors()
            .AddUnitOfWork<EwaysVNextDbContext>()
            .AddEvents(typeof(PricingErrors).Assembly);

        return services;
    }
}
