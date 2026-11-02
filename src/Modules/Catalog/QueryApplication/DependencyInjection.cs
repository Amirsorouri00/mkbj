using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using EwaysVNext.Modules.Catalog.QueryApplication.Products;
using EwaysVNext.Modules.Catalog.QueryApplication.Storefront;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Catalog.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<ICategoryQueries, CategoryQueries>();
        services.AddScoped<ICategoryAdminQueries, CategoryAdminQueries>();
        services.AddScoped<ICatalogAdminQueries, CatalogAdminQueries>();

        // Bound here rather than taken as a parameter: the section is optional, and a storefront
        // with no configuration serves media from the API's own path.
        services.AddOptions<StorefrontOptions>().BindConfiguration(StorefrontOptions.SectionName);
        services.AddScoped<IStorefrontQueries, StorefrontQueries>();

        return services;
    }
}
