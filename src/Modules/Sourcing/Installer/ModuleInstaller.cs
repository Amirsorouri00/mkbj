using EwaysVNext.Database;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Sourcing.Adapter.Inbound.Rest;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound;
using EwaysVNext.Modules.Sourcing.Application;
using EwaysVNext.Modules.Sourcing.QueryApplication;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sourcing;

public static class ModuleInstaller
{
    public static IServiceCollection AddSourcingModule(this IServiceCollection services)
    {
        services.AddSourcingApplication();
        services.AddSourcingOutboundAdapters();
        services.AddSourcingQueryApplication();
        services.AddSourcingRestAdapter();

        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<EwaysVNextDbContext>()
            .AddEvents(typeof(SourcingApplicationMarker).Assembly);

        return services;
    }
}
