using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using EwaysVNext.Modules.Catalog.QueryApplication.Products;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Catalog.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<ICategoryQueries, CategoryQueries>();
        services.AddScoped<ICategoryAdminQueries, CategoryAdminQueries>();
        services.AddScoped<IProductQueryService, ProductQueryService>();
        return services;
    }
}
