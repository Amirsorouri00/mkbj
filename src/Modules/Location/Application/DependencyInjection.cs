using Microsoft.Extensions.DependencyInjection.Extensions;
using EwaysVNext.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using EwaysVNext.Modules.Location.Contracts.Abstractions;
using EwaysVNext.Modules.Location.Application.Service;

namespace EwaysVNext.Modules.Location.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLocationApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<Ports.Inbound.IProvinceInboundPort, ProvinceService>();
        services.AddScoped<ILocationService, LocationService>();
        return services;
    }
}
