using EwaysVNext.Modules.Admin.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Admin.Application.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Admin.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddAdminOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<ISalesReadPort, SalesReadGateway>();
        services.AddScoped<ICustomerReadPort, CustomerReadGateway>();
        services.AddScoped<IAuditReadPort, AuditReadGateway>();

        return services;
    }
}
