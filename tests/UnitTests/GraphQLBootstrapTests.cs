using AwesomeAssertions;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

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
}
