using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Dependencies.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Repositories;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Repositories.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Categories;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Products;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IAttributeBlueprintRepository, AttributeBlueprintRepository>();
        services.AddScoped<IMeasurementUnitRepository, MeasurementUnitRepository>();
        services.AddScoped<IAttributeBlueprintDependencyGateway, UnavailableAttributeBlueprintDependencyGateway>();
        services.AddScoped<ICategoryAttributeAssignmentRepository, CategoryAttributeAssignmentRepository>();
        services.AddScoped<ICategoryAttributeExclusionRepository, CategoryAttributeExclusionRepository>();
        services.AddHostedService<ReservationExpirationService>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICategoryProductRepository, CategoryProductRepository>();
        services.AddScoped<ICatalogProductLookup, CatalogProductLookup>();
        return services;
    }
}
