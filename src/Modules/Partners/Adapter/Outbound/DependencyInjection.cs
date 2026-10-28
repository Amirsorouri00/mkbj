using EwaysVNext.Modules.Partners.Adapter.Outbound.Persistence;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Partners.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddPartnersOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<IPartnerRepository, PartnerRepository>();
        services.AddScoped<IApiKeyLookup, ApiKeyLookup>();

        return services;
    }
}
