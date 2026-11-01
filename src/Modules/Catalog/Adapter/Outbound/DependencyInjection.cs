using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Dependencies.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Repositories;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Repositories.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Repositories.Brands;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Media;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Storefront.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Categories;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Products;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IAttributeBlueprintRepository, AttributeBlueprintRepository>();
        services.AddScoped<IBrandRepository, BrandRepository>();
        services.AddScoped<IMeasurementUnitRepository, MeasurementUnitRepository>();
        services.AddScoped<IAttributeBlueprintDependencyGateway, UnavailableAttributeBlueprintDependencyGateway>();
        services.AddScoped<ICategoryAttributeAssignmentRepository, CategoryAttributeAssignmentRepository>();
        services.AddScoped<ICategoryAttributeExclusionRepository, CategoryAttributeExclusionRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICategoryProductRepository, CategoryProductRepository>();
        services.AddScoped<ICatalogProductLookup, CatalogProductLookup>();
        services.AddScoped<IStorefrontRefreshTrigger, StorefrontRefreshTrigger>();
        services.AddScoped<ICatalogChangeNotifier, CatalogChangeNotifier>();
        services.AddScoped<ICatalogMediaStore, ObjectStorageCatalogMediaStore>();
        return services;
    }
}
