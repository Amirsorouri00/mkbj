using AwesomeAssertions;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

public class CustomerAdminListGraphQLSchemaTests
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
    public async Task Customers_Query_Is_Discoverable_With_The_Expected_Filter_Arguments()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("customers(");
        sdl.Should().Contain("name: String");
        sdl.Should().Contain("family: String");
        sdl.Should().Contain("userCode: String");
        sdl.Should().Contain("nationalId: String");
        sdl.Should().Contain("phoneNumber: String");
        sdl.Should().Contain("fromDate: DateTime");
        sdl.Should().Contain("toDate: DateTime");
        sdl.Should().Contain("provinceId: Int");
        sdl.Should().Contain("cityId: Int");
        sdl.Should().Contain("regionId: Int");
        sdl.Should().Contain("registrationStatus: CustomerStatusType");
    }

    [Fact]
    public async Task Customers_Query_Returns_A_Paged_Segment_With_TotalCount()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("customers(").And.Contain("): CustomersCollectionSegment");
        sdl.Should().Contain("type CustomersCollectionSegment");
        sdl.Should().Contain("totalCount: Int!");
        sdl.Should().Contain("items: [Customer!]");
        sdl.Should().Contain("type CollectionSegmentInfo");
    }

    [Fact]
    public async Task Customers_Filter_Argument_Is_Optional_So_The_First_Page_Load_Can_Omit_It()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().MatchRegex(@"customers\([^)]*filter: CustomerAdminListFilterInput\)");
    }

    [Fact]
    public async Task Customer_Type_Exposes_The_Grid_Fields_Required_By_The_Story()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("type Customer {");
        foreach (var field in new[]
                 {
                     "id: ID!", "userCode: String!", "name: String", "family: String",
                     "nationalId: String", "phoneNumber: String!", "birthDate: LocalDate",
                     "registrationDate: DateTime!", "registrationStatus: CustomerStatusType",
                     "verificationStatus: VerificationStatusType", "provinceId: Int", "provinceName: String",
                     "cityId: Int", "cityName: String", "regionId: Int", "regionName: String"
                 })
        {
            sdl.Should().Contain(field);
        }
    }

    [Fact]
    public async Task CustomerStatusType_Enum_Exposes_The_Registration_Workflow_Values()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("enum CustomerStatusType");
        sdl.Should().Contain("PENDING_APPROVAL");
        sdl.Should().Contain("APPROVED");
        sdl.Should().Contain("REJECTED");
    }

    [Fact]
    public async Task VerificationStatusType_Enum_Is_Exposed_As_Its_Own_Independent_Field()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("enum VerificationStatusType");
        sdl.Should().Contain("NOT_VERIFIED");
        sdl.Should().Contain("VERIFIED");
        sdl.Should().Contain("VERIFIED_BY_AGENT");
    }
}
