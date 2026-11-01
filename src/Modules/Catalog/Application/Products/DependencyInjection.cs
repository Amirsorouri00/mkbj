using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Modules.Catalog.Application.Products;

public static class DependencyInjection
{
    public static IServiceCollection AddProductApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddOptions<CatalogMediaOptions>().BindConfiguration(CatalogMediaOptions.SectionName);
        services.AddScoped<ICatalogAdminService, CatalogAdminService>();
        services.AddScoped<ICatalogPublishingPort, CatalogPublishingService>();

        // One service behind two ports: both answer from the same definition of "sellable", and
        // a second implementation is how the two would quietly disagree.
        services.AddScoped<SellableVariantService>();
        services.AddScoped<ISellableVariantPort>(
            provider => provider.GetRequiredService<SellableVariantService>());
        services.AddScoped<IVariantLookupPort>(
            provider => provider.GetRequiredService<SellableVariantService>());
        return services;
    }
}
