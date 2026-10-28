using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Location.Adapter.Inbound.GraphQL;
using EwaysVNext.Modules.Location.Adapter.Inbound.Rest;
using EwaysVNext.Modules.Location.Adapter.Outbound;
using EwaysVNext.Modules.Location.Application;
using ProvinceAggregate = EwaysVNext.Modules.Location.Application.Domain.Locations.Province;

namespace EwaysVNext.Modules.Location;

public static class ModuleInstaller
{
    public static IServiceCollection AddLocationModule(this IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        services.AddLocationApplication(configuration: config);
        services.AddLocationOutboundAdapters(config, isDevelopment);
        services.AddLocationRestAdapter();
        services.AddLocationGraphQL();

        var applicationAssembly = typeof(ProvinceAggregate).Assembly;
        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddEvents(applicationAssembly);

        return services;
    }
}
