using EwaysVNext.Database;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound;
using EwaysVNext.Modules.Sourcing.Application;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sourcing;

public static class ModuleInstaller
{
    public static IServiceCollection AddSourcingModule(this IServiceCollection services)
    {
        services.AddSourcingApplication();
        services.AddSourcingOutboundAdapters();

        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<EwaysVNextDbContext>()
            .AddEvents(typeof(SourcingApplicationMarker).Assembly);

        return services;
    }
}
