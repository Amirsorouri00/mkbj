using AwesomeAssertions;
using Mkbj.Presentation.Endpoint.GraphQL;
using Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mkbj.UnitTests;

public class GraphQLBootstrapTests
{
    [Fact]
    public async Task AddGraphQLBootstrap_Is_Idempotent_When_Called_By_More_Than_One_Module()
    {
        var services = new ServiceCollection();

        services.AddCatalogGraphQL();
        var builder = services.AddGraphQLBootstrap();

        var act = async () => await builder.BuildSchemaAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Category_attribute_snowflake_ids_are_exposed_as_graphql_ids()
    {
        var services = new ServiceCollection();
        services.AddCatalogGraphQL();

        var schema = (await services.AddGraphQLBootstrap().BuildSchemaAsync()).ToString();

        schema.Should().Contain("effectiveCategoryAttributes(categoryId: ID!)");
        schema.Should().Contain("assignCategoryAttribute(");
        schema.Should().Contain("categoryId: ID!");
        schema.Should().Contain("attributeBlueprintId: ID!");
        schema.Should().Contain("excludedSourceAssignmentIds: [ID!]!");
        schema.Should().Contain("type EffectiveCategoryAttributeSnapshot {")
            .And.Contain("assignmentId: ID!")
            .And.Contain("sourceCategoryId: ID!");
    }

    [Fact]
    public async Task Variant_lookup_is_exposed_for_admin_autocomplete_with_graphql_ids()
    {
        var services = new ServiceCollection();
        services.AddCatalogGraphQL();

        var schema = (await services.AddGraphQLBootstrap().BuildSchemaAsync()).ToString();

        schema.Should().Contain("adminVariantLookup(query: String!, take: Int!)");
        schema.Should().Contain("type ProductAdminVariantLookupDto {")
            .And.Contain("variantId: ID!")
            .And.Contain("productId: ID!");
    }
}
