using Mkbj.Database;
using Mkbj.DependencyInjection;
using Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL;
using Mkbj.Modules.Catalog.Adapter.Inbound.Rest;
using Mkbj.Modules.Catalog.Adapter.Outbound;
using Mkbj.Modules.Catalog.Application.AttributeBlueprints;
using Mkbj.Modules.Catalog.Application.Brands;
using Mkbj.Modules.Catalog.Application.CategoryAttributeAssignments;
using Mkbj.Modules.Catalog.Application.CategoryAttributeExclusions;
using Mkbj.Modules.Catalog.Application.Categories;
using Mkbj.Modules.Catalog.Application.Products;
using Mkbj.Modules.Catalog.Application.Products.Domain;
using Mkbj.Modules.Catalog.QueryApplication;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Catalog;

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
            .AddUnitOfWork<MkbjDbContext>()
            .AddEvents(applicationAssembly);

        return services;
    }
}
