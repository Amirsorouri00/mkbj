using Mkbj.Modules.Settings.Adapter.Inbound.Rest.Admin;
using Mkbj.Presentation.Endpoint;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Settings.Adapter.Inbound.Rest;

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
