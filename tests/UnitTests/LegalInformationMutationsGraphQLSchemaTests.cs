using AwesomeAssertions;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

public class LegalInformationMutationsGraphQLSchemaTests
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
    public async Task ApproveLegalInformation_Mutation_Is_Discoverable_And_Requires_The_Admin_Policy()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("approveLegalInformation(").And.Contain("customerId: Long!");
        sdl.Should().Contain("): LegalInformationResponse!");

        var fieldStart = sdl.IndexOf("approveLegalInformation(", StringComparison.Ordinal);
        var nextFieldStart = sdl.IndexOf("rejectLegalInformation(", StringComparison.Ordinal);
        sdl[fieldStart..nextFieldStart].Should().Contain("@authorize(policy: \"Admin\")");
    }

    [Fact]
    public async Task RejectLegalInformation_Mutation_Is_Discoverable_And_Requires_The_Admin_Policy()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("rejectLegalInformation(");
        sdl.Should().Contain("input: RejectLegalInformationInput!");
        sdl.Should().Contain("): LegalInformationResponse!");
        sdl.Should().Contain("input RejectLegalInformationInput");
        sdl.Should().Contain("reason: String!");
        sdl.Should().Contain("editableFields: [LegalInformationEditableField!]!");

        var fieldStart = sdl.IndexOf("rejectLegalInformation(", StringComparison.Ordinal);
        var fieldEnd = fieldStart + 200;
        sdl[fieldStart..Math.Min(fieldEnd, sdl.Length)].Should().Contain("@authorize(policy: \"Admin\")");
    }

    [Fact]
    public async Task LegalInformationResponse_Exposes_The_Business_Level_Read_Model()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("type LegalInformationResponse");
        foreach (var field in new[] { "status: String!", "rejectionReason: String", "canSubmit: Boolean!", "documents:" })
            sdl.Should().Contain(field);
    }
}
