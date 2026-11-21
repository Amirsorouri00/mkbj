using Mkbj.Modules.Admin.Adapter.Outbound.Gateways;
using Mkbj.Modules.Admin.Application.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Admin.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddAdminOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<ISalesReadPort, SalesReadGateway>();
        services.AddScoped<ISourcingReadPort, SourcingReadGateway>();
        services.AddScoped<ICustomerReadPort, CustomerReadGateway>();
        services.AddScoped<IAuditReadPort, AuditReadGateway>();

        return services;
    }
}
