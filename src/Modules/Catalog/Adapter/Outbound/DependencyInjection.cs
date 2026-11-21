using Mkbj.Jobs;
using Mkbj.Jobs.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Mkbj.Modules.Catalog.Adapter.Outbound.Dependencies.AttributeBlueprints;
using Mkbj.Modules.Catalog.Adapter.Outbound.Repositories;
using Mkbj.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using Mkbj.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Outbound;
using Mkbj.Modules.Catalog.Adapter.Outbound.Repositories.AttributeBlueprints;
using Mkbj.Modules.Catalog.Adapter.Outbound.Repositories.Brands;
using Mkbj.Modules.Catalog.Adapter.Outbound.Media;
using Mkbj.Modules.Catalog.Adapter.Outbound.Storefront;
using Mkbj.Modules.Catalog.Application.Products.Ports.Outbound;
using Mkbj.Modules.Catalog.Application.Storefront.Ports.Inbound;
using Mkbj.Modules.Catalog.Application.Brands.Ports.Outbound;
using Mkbj.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using Mkbj.Modules.Catalog.Adapter.Outbound.Persistence.Categories;
using Mkbj.Modules.Catalog.Adapter.Outbound.Persistence.Products;
using Mkbj.Modules.Catalog.Application.Categories.Ports.Outbound;

namespace Mkbj.Modules.Catalog.Adapter.Outbound;

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
        services.AddScoped<ISellableVariantReader, SellableVariantReader>();
        services.AddScoped<IStorefrontViewRefresher, StorefrontViewRefresher>();

        // The 24-hour backstop. The debounced trigger covers ordinary edits; this catches a
        // refresh that was dropped, and anything that changed the views without going through the
        // catalogue — an expiring FX rate ages a row out with no edit behind it.
        services.AddJobHandler<RefreshStorefrontViewsJobHandler, RefreshStorefrontViewsPayload>();
        services.AddRecurringJob(JobTypes.RefreshStorefrontViews, TimeSpan.FromHours(24));

        return services;
    }
}
