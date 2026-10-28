using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL;
using EwaysVNext.Modules.Customer.Adapter.Inbound.Rest;
using EwaysVNext.Modules.Customer.Adapter.Outbound;
using EwaysVNext.Modules.Customer.Application;

namespace EwaysVNext.Modules.Customer;

public static class ModuleInstaller
{
    public static IServiceCollection AddCustomerModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddCustomerApplication(configuration: config);
        services.AddCustomerOutboundAdapters(config);
        services.AddCustomerRestAdapter();
        services.AddCustomerGraphQL();
        services.AddUserProfileService();
        services.AddFileTypeDetection();

        return services;
    }
}
