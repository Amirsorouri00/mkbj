using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EwaysVNext.IntegrationTests.Catalog;

/// <summary>
/// The catalogue's admin REST surface over HTTP. Brands are everyday catalogue work, so these
/// are gated at staff rather than super admin — which is itself one of the things asserted here.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class CatalogAdminRestTests(PostgresFixture fixture)
{
    private const string Base = "/api/v1/admin/catalog/brands";

    private static HttpClient StaffClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateStaffToken());

        return client;
    }

    private static string Name(string prefix) => $"{prefix} {Guid.NewGuid().ToString("N")[..8]}";

    [Fact]
    public async Task Staff_Creates_Brand_Returns_201()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);

        using var response = await client.PostAsJsonAsync(
            Base, new { displayName = Name("Northwind"), aliases = new[] { Name("North Wind") } });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull(
            "a created resource says where it is, and the admin UI follows it");
    }

    [Fact]
    public async Task Anonymous_Create_Brand_Returns_401()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Base, new { displayName = Name("Sneaky") });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_Shop_Customer_Cannot_Reach_The_Brand_Surface()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateCustomerToken());

        using var response = await client.GetAsync(Base);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the Shop realm is open self-service registration, so a token minted there carries "
            + "no backoffice meaning whatever its roles say");
    }

    [Fact]
    public async Task A_Name_Another_Brand_Already_Uses_Returns_409()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);
        var name = Name("Acme");

        using var first = await client.PostAsJsonAsync(Base, new { displayName = name });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await client.PostAsJsonAsync(Base, new { displayName = name });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("BRAND_NAME_DUPLICATE");
    }

    [Fact]
    public async Task A_Name_Used_As_Another_Brands_Alias_Returns_409()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);
        var alias = Name("Nordwind");

        using var first = await client.PostAsJsonAsync(
            Base, new { displayName = Name("Northwind"), aliases = new[] { alias } });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await client.PostAsJsonAsync(Base, new { displayName = alias });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the alias index is global, so an alias makes that name unavailable everywhere");
    }

    [Fact]
    public async Task The_Listing_Finds_A_Brand_By_Its_Alias()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);
        var display = Name("Kettleworks");
        var alias = Name("Kettle Works");

        using var created = await client.PostAsJsonAsync(
            Base, new { displayName = display, aliases = new[] { alias } });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        // Searching by alias is what answers "why can't I use this name?".
        using var response = await client.GetAsync($"{Base}?q={Uri.EscapeDataString(alias.ToLowerInvariant().Replace(' ', '-'))}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("items");

        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("displayName").GetString().Should().Be(display);
    }

    [Fact]
    public async Task Renaming_A_Brand_That_Does_Not_Exist_Returns_404()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);

        using var response = await client.PutAsJsonAsync(
            $"{Base}/999999999", new { displayName = Name("Ghost") });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Brand_Can_Be_Deactivated_And_Reactivated()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);

        using var created = await client.PostAsJsonAsync(Base, new { displayName = Name("Switchable") });
        var id = created.Headers.Location!.ToString().Split('/')[^1];

        using var off = await client.PutAsJsonAsync($"{Base}/{id}/status", new { isActive = false });
        off.StatusCode.Should().Be(HttpStatusCode.OK);

        using var on = await client.PutAsJsonAsync($"{Base}/{id}/status", new { isActive = true });
        on.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
