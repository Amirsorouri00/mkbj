using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Brands;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Categories;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.CategoryAttributeAssignment;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.CategoryAttributeExclusion;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogGraphQL(this IServiceCollection services)
    {
        services
            .AddGraphQLBootstrap()
            .AddProjections()
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
            .AddType<CategoryAttributeExclusionMutationResolvers>()
            .AddType<CategoryAttributeExclusionQueryResolvers>();

        return services;
    }
}
