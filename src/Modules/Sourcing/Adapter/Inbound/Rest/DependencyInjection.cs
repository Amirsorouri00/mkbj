using EwaysVNext.Presentation.Endpoint;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sourcing.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddSourcingRestAdapter(this IServiceCollection services)
    {
        services.AddEndpoints(typeof(DependencyInjection).Assembly);
        return services;
    }
}
