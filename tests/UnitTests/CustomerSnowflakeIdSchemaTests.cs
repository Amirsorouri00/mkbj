using System.Text.RegularExpressions;
using AwesomeAssertions;
using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

public class CustomerSnowflakeIdSchemaTests
{
    private static readonly Lazy<Task<string>> Sdl = new(async () =>
    {
        var services = new ServiceCollection();
        services.AddCustomerGraphQL();
        var schema = await services.AddGraphQLBootstrap().BuildSchemaAsync();
        return schema.ToString();
    });

    [Theory]
    [InlineData("type Customer", "id: ID!")]
    [InlineData("type TeamMemberListItem", "id: ID!")]
    [InlineData("type CustomerRegistrationReview", "customerId: ID!")]
    [InlineData("type UpdateCustomerStatusPayload", "customerId: ID!")]
    [InlineData("type UpdateCustomerVerificationStatusPayload", "customerId: ID!")]
    [InlineData("type CreateTeamMemberPayload", "id: ID!")]
    [InlineData("input UpdateCustomerStatusInput", "customerId: ID!")]
    [InlineData("input UpdateCustomerVerificationStatusInput", "customerId: ID!")]
    [InlineData("input UpdateTeamMemberInput", "id: ID!")]
    [InlineData("input DeleteTeamMemberInput", "id: ID!")]
    [InlineData("type Query", "customerRegistrationReview(customerId: ID!)")]
    public async Task Every_Customer_Snowflake_Id_Is_Exposed_As_ID(string typeHeader, string member)
    {
        var block = TypeBlock(await Sdl.Value, typeHeader);

        block.Should().MatchRegex(@"(?m)^\s*" + Regex.Escape(member));
    }

    private static string TypeBlock(string sdl, string typeHeader)
    {
        var match = Regex.Match(sdl, @"(?ms)^" + Regex.Escape(typeHeader) + @"\b[^{]*\{(.*?)^\}");
        match.Success.Should().BeTrue($"the schema should declare `{typeHeader}`");
        return match.Groups[1].Value;
    }
}
