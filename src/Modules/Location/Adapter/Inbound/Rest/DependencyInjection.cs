using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Modules.Location.Adapter.Inbound.Rest.Endpoints;

namespace EwaysVNext.Modules.Location.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddLocationRestAdapter(this IServiceCollection services)
    {
        services.AddEndpoints(typeof(LocationEndpoints).Assembly);
        return services;
    }
}
