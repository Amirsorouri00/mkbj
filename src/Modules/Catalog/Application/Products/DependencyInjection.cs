using Microsoft.Extensions.DependencyInjection.Extensions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Services;
using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Modules.Catalog.Application.Products.Validation;
using EwaysVNext.Modules.Catalog.Contracts.Products;

namespace EwaysVNext.Modules.Catalog.Application.Products;

public static class DependencyInjection
{
    public static IServiceCollection AddProductApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<ProductService>();
        services.AddScoped<IProductService>(
            provider => provider.GetRequiredService<ProductService>());
        services.AddScoped<IProductAvailabilityContract>(
            provider => provider.GetRequiredService<ProductService>());
        services.AddScoped<CreateProductValidator>();
        services.AddScoped<ChangePriceValidator>();
        services.AddScoped<ICatalogAdminService, CatalogAdminService>();

        return services;
    }
}
