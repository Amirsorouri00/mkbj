using Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.AttributeBlueprints;
using Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.Brands;
using Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.Products;
using Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.Shop;
using Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.Categories;
using Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.CategoryAttributeAssignment;
using Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.CategoryAttributeExclusion;
using Mkbj.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogGraphQL(this IServiceCollection services)
    {
        services
            .AddGraphQLBootstrap()
            .AddProjections()
            // Uploads travel as multipart; without this the Upload scalar has no type.
            .AddUploadType()
            .AddType<ProductAdminQueries>()
            .AddType<ProductAdminMutations>()
            .AddType<ProductAdminRowType>()
            .AddType<ProductAdminDetailType>()
            .AddType<ProductAdminVariantType>()
            .AddType<ProductAdminVariantLookupType>()
            .AddType<ProductAdminIdentifierType>()
            .AddType<ProductAdminMediaType>()
            .AddType<CategoryAdminType>()
            .AddType<CategoryAdminParentType>()
            .AddType<CategoryAdminTreeNodeType>()
            .AddType<BrandAdminGraphQLQueries>()
            .AddType<BrandAdminGraphQLMutations>()
            .AddType<CategoryAdminQueries>()
            .AddType<CategoryAdminMutations>()
            .AddType<AttributeBlueprintQueries>()
            .AddType<AttributeBlueprintMutations>()
            .AddType<AttributeBlueprintType>()
            .AddType<AttributeBlueprintOptionType>()
            .AddType<MeasurementUnitType>()
            .AddType<CategoryAttributeAssignmentQueryResolvers>()
            .AddType<CategoryAttributeAssignmentMutationResolvers>()
            .AddType<CategoryAttributeAssignmentSnapshotType>()
            .AddType<CategoryAttributeAssignmentAdminRowType>()
            .AddType<EffectiveCategoryAttributeSnapshotType>()
            .AddType<CategoryAttributeExclusionSnapshotType>()
            .AddType<CategoryAttributeExclusionMutationResolvers>()
            .AddType<CategoryAttributeExclusionQueryResolvers>()
            .AddType<ShopProductCardType>()
            .AddType<ShopCategoryTileType>()
            .AddType<ShopCategoryCrumbType>()
            .AddType<ShopVariantOfferType>()
            .AddType<ShopProductDetailType>()
            .AddType<ShopCategoryTreeNodeType>()
            .AddType<ShopQueries>();

        return services;
    }
}
