using Mkbj.Database;
using Mkbj.DependencyInjection;
using Mkbj.Modules.Sourcing.Adapter.Inbound.Rest;
using Mkbj.Modules.Sourcing.Adapter.Outbound;
using Mkbj.Modules.Sourcing.Application;
using Mkbj.Modules.Sourcing.QueryApplication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Sourcing;

public static class ModuleInstaller
{
    public static IServiceCollection AddSourcingModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSourcingApplication();
        services.AddSourcingOutboundAdapters(configuration);
        services.AddSourcingQueryApplication();
        services.AddSourcingRestAdapter();

        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<MkbjDbContext>()
            .AddEvents(typeof(SourcingApplicationMarker).Assembly);

        return services;
    }
}
