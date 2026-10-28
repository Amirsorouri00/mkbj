using AwesomeAssertions;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL;
using EwaysVNext.Modules.Location.Adapter.Inbound.GraphQL;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

public class LocationGraphQLSchemaTests
{
    // Location contributes no mutations yet, so building its schema alone (without another
    // module supplying at least one mutation field) trips HotChocolate's "root type can't be
    // empty" validation on the shared Mutation type. Composing with Customer's GraphQL module
    // both sidesteps that and doubles as a regression test for the two modules sharing one schema.
    private static async Task<string> BuildSchemaSdlAsync()
    {
        var services = new ServiceCollection();
        services.AddLocationGraphQL();
        services.AddCustomerGraphQL();
        var builder = services.AddGraphQLBootstrap();

        var schema = await builder.BuildSchemaAsync();
        return schema.ToString();
    }

    [Fact]
    public async Task Provinces_Query_Is_Discoverable_With_An_Optional_Name_Filter()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("provinces(name: String").And.Contain("): [Province!]!");
    }

    [Fact]
    public async Task CitiesByProvinceId_Query_Is_Discoverable_With_A_Required_ProvinceId()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("citiesByProvinceId(provinceId: Int!): [City!]!");
    }

    [Fact]
    public async Task DistrictsByCityId_Query_Is_Discoverable_With_A_Required_CityId()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("districtsByCityId(cityId: Int!): [District!]!");
    }

    [Fact]
    public async Task CityById_Query_Returns_A_Nullable_City()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("cityById(cityId: Int!): City").And.NotContain("cityById(cityId: Int!): City!");
    }

    [Fact]
    public async Task Province_Type_Exposes_Scalar_Fields_But_Not_Cities()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("type Province {");
        foreach (var field in new[] { "id: Int!", "name: String!", "code: String", "isActive: Boolean!" })
            sdl.Should().Contain(field);

        var provinceTypeStart = sdl.IndexOf("type Province {", StringComparison.Ordinal);
        var provinceTypeEnd = sdl.IndexOf('}', provinceTypeStart);
        sdl[provinceTypeStart..provinceTypeEnd].Should().NotContain("cities");
    }

    [Fact]
    public async Task City_Type_Exposes_Its_Province_And_Districts()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("type City {");
        sdl.Should().Contain("province: Province!");
        sdl.Should().Contain("districts: [District!]!");
    }

    [Fact]
    public async Task District_Type_Exposes_Its_City()
    {
        var sdl = await BuildSchemaSdlAsync();

        sdl.Should().Contain("type District {");
        sdl.Should().Contain("city: City!");
    }
}
