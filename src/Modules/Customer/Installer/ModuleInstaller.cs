using Mkbj.Database;
using Mkbj.DependencyInjection;
using Mkbj.Modules.Customer.Adapter.Inbound.GraphQL;
using Mkbj.Modules.Customer.Adapter.Inbound.Rest;
using Mkbj.Modules.Customer.Adapter.Outbound;
using Mkbj.Modules.Customer.Application;
using Mkbj.Modules.Customer.QueryApplication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using CustomerAggregate = Mkbj.Modules.Customer.Application.Customers.Domain.Customer;

namespace Mkbj.Modules.Customer;

public static class ModuleInstaller
{
    public static IServiceCollection AddCustomerModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCustomerApplication();
        services.AddCustomerOutboundAdapters();
        services.AddCustomerRestAdapter();
        services.AddCustomerGraphQL();
        services.AddCustomerQueryApplication();

        var applicationAssembly = typeof(CustomerAggregate).Assembly;
        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<MkbjDbContext>()
            .AddEvents(applicationAssembly);

        return services;
    }
}
