using EwaysVNext.Database;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Partners.Adapter.Outbound;
using EwaysVNext.Modules.Partners.Application;
using EwaysVNext.Modules.Partners.Application.Partners.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Partners;

public static class ModuleInstaller
{
    public static IServiceCollection AddPartnersModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPartnersApplication(configuration);
        services.AddPartnersOutboundAdapters();

        var applicationAssembly = typeof(Partner).Assembly;
        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<EwaysVNextDbContext>()
            .AddEvents(applicationAssembly);

        return services;
    }
}
