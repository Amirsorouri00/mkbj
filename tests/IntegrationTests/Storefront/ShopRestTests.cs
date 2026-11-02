using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.IntegrationTests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Storefront;

/// <summary>
/// The storefront over HTTP, as an unauthenticated visitor sees it.
/// </summary>
/// <remarks>
/// Anonymous throughout: a shop that demands a token to show a product page is a shop nobody
/// indexes and nobody shares a link to. Each test therefore uses a bare client, and a 401 here
/// would be a defect rather than a policy.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class ShopRestTests(PostgresFixture fixture)
{
    private const string BasePath = "/api/v1/shop";

    private static readonly string[] Currencies = ["USD", "EUR", "SGD", "AED", "GBP"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Plp_Never_Shows_Draft_Or_Deactivated()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        using var client = factory.CreateClient();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);

        foreach (var currency in Currencies)
        {
            using var response = await client.GetAsync(
                $"{BasePath}/products?currency={currency}&categoryId={scenario.RootCategoryId}", Ct);

            response.StatusCode.Should().Be(HttpStatusCode.OK, "the storefront is anonymous");

            var ids = await ProductIdsAsync(response);
            ids.Should().BeSubsetOf([scenario.P1, scenario.P2],
                "P3 is Draft and P4 was taken down, in {0} as in every other currency", currency);
        }
    }

    [Fact]
    public async Task Plp_Attribute_And_Price_Query_Params_Are_Applied()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        using var client = factory.CreateClient();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);

        using var byAttribute = await client.GetAsync(
            $"{BasePath}/products?currency=USD&categoryId={scenario.RootCategoryId}&attr.colour=Blue", Ct);

        (await ProductIdsAsync(byAttribute)).Should().BeEquivalentTo([scenario.P1]);

        using var byPrice = await client.GetAsync(
            $"{BasePath}/products?currency=USD&categoryId={scenario.RootCategoryId}&priceMin=26.00", Ct);

        (await ProductIdsAsync(byPrice)).Should().BeEquivalentTo([scenario.P2]);
    }

    [Theory]
    [InlineData("priceMin=10")]
    [InlineData("priceMax=99.9")]
    [InlineData("sort=cheapest")]
    public async Task A_Malformed_Filter_Is_400(string filter)
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"{BasePath}/products?currency=USD&{filter}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Pdp_By_Slug_Is_Anonymous_200()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        using var client = factory.CreateClient();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);
        var slug = await SlugAsync(client, scenario.RootCategoryId, scenario.P1);

        using var response = await client.GetAsync($"{BasePath}/products/{slug}?currency=USD", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("data");
        body.GetProperty("productId").GetInt64().Should().Be(scenario.P1);
        body.GetProperty("variants").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Unknown_Slug_Is_404_Not_Found()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"{BasePath}/products/no-such-kettle?currency=USD", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Missing_Currency_Is_Rejected_Rather_Than_Defaulted()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"{BasePath}/home", Ct);

        // Guessing a currency would show every price in a unit the visitor never chose.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("CURRENCY_NOT_ENABLED");
    }

    [Fact]
    public async Task Search_Endpoint_Returns_Next_Cursor()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        using var client = factory.CreateClient();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);
        _ = scenario;

        using var response = await client.GetAsync(
            $"{BasePath}/search?q=kettle&currency=USD&limit=1", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("data");
        data.GetProperty("items").GetArrayLength().Should().Be(1);
        data.GetProperty("nextCursor").GetString().Should().NotBeNullOrEmpty(
            "the scenario alone puts more than one kettle in the shop");
    }

    [Fact]
    public async Task Facets_And_Categories_Are_Anonymous()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        using var client = factory.CreateClient();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);

        using var facets = await client.GetAsync(
            $"{BasePath}/facets?currency=USD&categoryId={scenario.RootCategoryId}", Ct);
        facets.StatusCode.Should().Be(HttpStatusCode.OK);

        using var categories = await client.GetAsync($"{BasePath}/categories", Ct);
        categories.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The shop stays open even if the application later adopts a fallback authorization policy.
    /// </summary>
    /// <remarks>
    /// Without this, <c>.AllowAnonymous()</c> on these routes guards nothing that can be observed:
    /// there is no fallback policy today, so removing every one of those calls leaves the whole
    /// suite green. The day someone adds one — a reasonable thing to want — the storefront would
    /// start demanding a token, and the first report would come from a search engine.
    /// </remarks>
    [Fact]
    public async Task The_Shop_Stays_Open_Under_A_Fallback_Authorization_Policy()
    {
        using var factory = fixture.CreateFactory(configureServices: services =>
            services.Configure<AuthorizationOptions>(options =>
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser().Build()));

        using var scope = factory.Services.CreateScope();
        using var client = factory.CreateClient();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);

        using var products = await client.GetAsync(
            $"{BasePath}/products?currency=USD&categoryId={scenario.RootCategoryId}", Ct);
        products.StatusCode.Should().Be(HttpStatusCode.OK);

        using var home = await client.GetAsync($"{BasePath}/home?currency=USD", Ct);
        home.StatusCode.Should().Be(HttpStatusCode.OK);

        using var categories = await client.GetAsync($"{BasePath}/categories", Ct);
        categories.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<string> SlugAsync(HttpClient client, long categoryId, long productId)
    {
        using var response = await client.GetAsync(
            $"{BasePath}/products?currency=USD&categoryId={categoryId}", Ct);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("productId").GetInt64() == productId)
            .GetProperty("slug").GetString()!;
    }

    private static async Task<List<long>> ProductIdsAsync(HttpResponseMessage response) =>
        [.. (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("productId").GetInt64())];

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
