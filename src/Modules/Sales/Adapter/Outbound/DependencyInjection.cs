using EwaysVNext.Modules.Sales.Adapter.Outbound.Persistence;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddSalesOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IDocumentNumberAllocator, DocumentNumberAllocator>();

        return services;
    }
}
