using EwaysVNext.Modules.Customer.Adapter.Outbound.Persistence;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Customer.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<ICustomerRepository, CustomerRepository>();

        return services;
    }
}
