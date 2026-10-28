using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Endpoints;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerRestAdapter(this IServiceCollection services)
    {
        services.AddEndpoints(typeof(CustomersEndpoints).Assembly);
        return services;
    }
}
