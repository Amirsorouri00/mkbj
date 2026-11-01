using EwaysVNext.Database;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest;
using EwaysVNext.Modules.Catalog.Adapter.Outbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.Application.Brands;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions;
using EwaysVNext.Modules.Catalog.Application.Categories;
using EwaysVNext.Modules.Catalog.Application.Products;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.QueryApplication;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Catalog;

public static class ModuleInstaller
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddCatalogOutboundAdapters();
        services.AddCatalogRestAdapter();
        services.AddCatalogGraphQL();
        services.AddCatalogQueryApplication();

        services.AddCategoryApplication();

        services.AddProductApplication();
        services.AddBrandApplication();

        services.AddAttributeBlueprintApplication();
        services.AddCategoryAttributeAssignmentApplication();
        services.AddCategoryAttributeExclusionApplication();

        var applicationAssembly = typeof(Product).Assembly;
        services
            // Product images are type-sniffed from their bytes before they reach storage.
            .AddFileTypeDetection()
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<EwaysVNextDbContext>()
            .AddEvents(applicationAssembly);

        return services;
    }
}
