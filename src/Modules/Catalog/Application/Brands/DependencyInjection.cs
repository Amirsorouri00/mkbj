using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Brands.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Catalog.Application.Brands;

public static class DependencyInjection
{
    public static IServiceCollection AddBrandApplication(this IServiceCollection services)
    {
        services.AddScoped<IBrandAdminService, BrandAdminService>();
        return services;
    }
}
