using Mkbj.Modules.Customer.Adapter.Outbound.Persistence;
using Mkbj.Modules.Customer.Application.Customers.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Customer.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<ICustomerRepository, CustomerRepository>();

        return services;
    }
}
