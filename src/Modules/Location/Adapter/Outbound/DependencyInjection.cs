using EwaysVNext.Modules.Location.Adapter.Outbound.Repositories;
using EwaysVNext.Modules.Location.Application.Ports.Outbound;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Location.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddLocationOutboundAdapters(this IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        services.AddScoped<IProvinceRepository, ProvinceRepository>();
        services.AddScoped<IAddressRepository, AddressRepository>();

        return services;
    }
}
