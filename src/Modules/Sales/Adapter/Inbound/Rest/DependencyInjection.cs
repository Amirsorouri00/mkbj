using EwaysVNext.Presentation.Endpoint;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    /// <summary>
    /// The host scans only its own assembly for endpoints, so a module that does not register
    /// its own returns 404 with an empty body rather than failing at startup — the phase 1
    /// partner routes did exactly that.
    /// </summary>
    public static IServiceCollection AddSalesRestAdapter(this IServiceCollection services)
    {
        services.AddEndpoints(typeof(DependencyInjection).Assembly);
        return services;
    }
}
