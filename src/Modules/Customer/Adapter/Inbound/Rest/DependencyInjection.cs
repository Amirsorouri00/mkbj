using EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Shop;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerRestAdapter(this IServiceCollection services)
    {
        // Discovery scans one assembly at a time and the Host only scans its own.
        services.AddEndpoints(typeof(ShopMeEndpoints).Assembly);

        return services;
    }
}
