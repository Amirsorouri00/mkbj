using AwesomeAssertions;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

public class CustomerMutationsGraphQLSchemaTests
{
    private static async Task<string> BuildSchemaSdlAsync()
    {
        var services = new ServiceCollection();
        services.AddCustomerGraphQL();
        var builder = services.AddGraphQLBootstrap();

        var schema = await builder.BuildSchemaAsync();
        return schema.ToString();
    }

    [Fact]
    public async Task UpdateCustomerStatus_Mutation_Is_Discoverable_With_The_Expected_Shape()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("updateCustomerStatus(input: UpdateCustomerStatusInput!): UpdateCustomerStatusPayload!");
        sdl.Should().Contain("input UpdateCustomerStatusInput");
        sdl.Should().Contain("customerId: ID!");
        sdl.Should().Contain("status: CustomerStatusType!");
        sdl.Should().Contain("type UpdateCustomerStatusPayload");
        sdl.Should().Contain("success: Boolean!");
    }

    [Fact]
    public async Task UpdateCustomerVerificationStatus_Mutation_Is_Discoverable_With_The_Expected_Shape()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("updateCustomerVerificationStatus(");
        sdl.Should().Contain("input: UpdateCustomerVerificationStatusInput!");
        sdl.Should().Contain("): UpdateCustomerVerificationStatusPayload!");
        sdl.Should().Contain("input UpdateCustomerVerificationStatusInput");
        sdl.Should().Contain("type UpdateCustomerVerificationStatusPayload");
    }
}
