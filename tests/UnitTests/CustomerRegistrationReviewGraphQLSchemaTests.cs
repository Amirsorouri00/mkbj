using AwesomeAssertions;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

public class CustomerRegistrationReviewGraphQLSchemaTests
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
    public async Task CustomerRegistrationReview_Query_Is_Discoverable_And_Takes_A_CustomerId()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("customerRegistrationReview(").And.Contain("customerId: ID!");
        sdl.Should().Contain("): CustomerRegistrationReview");
    }

    [Fact]
    public async Task CustomerRegistrationReview_Exposes_Registration_Verification_Status_Method_And_Reason()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("type CustomerRegistrationReview {");
        foreach (var field in new[]
                 {
                     "registrationDate: DateTime!", "verificationDate: DateTime", "reason: String",
                     "verificationStatus: CustomerStatusType", "verificationMethod: VerificationMethodType"
                 })
        {
            sdl.Should().Contain(field);
        }
    }

    [Fact]
    public async Task CustomerReviewHistory_No_Longer_Exists_In_The_Schema()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().NotContain("CustomerReviewHistory");
        sdl.Should().NotContain("customerReviewHistory");
    }
}
