using EwaysVNext.Presentation.Endpoint;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Admin.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    /// <summary>
    /// The host scans only its own assembly for endpoints, so a module that does not register
    /// its own returns 404 with an empty body rather than failing at startup.
    /// </summary>
    public static IServiceCollection AddAdminRestAdapter(this IServiceCollection services)
    {
        services.AddEndpoints(typeof(DependencyInjection).Assembly);
        return services;
    }
}
