using EwaysVNext.Database;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Customer.Adapter.Outbound;
using EwaysVNext.Modules.Customer.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer;

namespace EwaysVNext.Modules.Customer;

public static class ModuleInstaller
{
    public static IServiceCollection AddCustomerModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCustomerApplication();
        services.AddCustomerOutboundAdapters();

        var applicationAssembly = typeof(CustomerAggregate).Assembly;
        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<EwaysVNextDbContext>()
            .AddEvents(applicationAssembly);

        return services;
    }
}
