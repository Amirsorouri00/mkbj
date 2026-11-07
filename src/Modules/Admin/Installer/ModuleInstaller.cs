using EwaysVNext.Modules.Admin.Adapter.Inbound.GraphQL;
using EwaysVNext.Modules.Admin.Adapter.Inbound.Rest;
using EwaysVNext.Modules.Admin.Adapter.Outbound;
using EwaysVNext.Modules.Admin.Application;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Admin;

public static class ModuleInstaller
{
    /// <summary>
    /// Wires the composition module.
    /// </summary>
    /// <remarks>
    /// No <c>AddUnitOfWork</c>, no interceptors, no events: Admin owns no data and writes
    /// nothing. It reads other modules' read sides and arranges what they return.
    /// </remarks>
    public static IServiceCollection AddAdminModule(this IServiceCollection services)
    {
        services.AddAdminApplication();
        services.AddAdminOutboundAdapters();
        services.AddAdminRestAdapter();
        services.AddAdminGraphQL();

        return services;
    }
}
