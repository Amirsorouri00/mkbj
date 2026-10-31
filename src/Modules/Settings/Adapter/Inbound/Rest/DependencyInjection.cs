using EwaysVNext.Modules.Settings.Adapter.Inbound.Rest.Admin;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Settings.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddSettingsRestAdapter(this IServiceCollection services)
    {
        // Discovery scans one assembly at a time and the Host only scans its own, so a module's
        // endpoints register themselves here or they are never mapped.
        services.AddEndpoints(typeof(SettingsAdminEndpoints).Assembly);

        return services;
    }
}
