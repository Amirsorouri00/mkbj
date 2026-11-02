using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Storefront;

/// <summary>
/// The storefront over GraphQL, which must answer exactly what REST answers.
/// </summary>
/// <remarks>
/// Two surfaces over one query layer is the deal the PRD asks for, and nothing but a test keeps
/// them in step — a client choosing GraphQL should never get a different shop.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class ShopGraphQLTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShopProducts_Matches_Rest_For_Same_Filter()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        using var client = factory.CreateClient();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);

        const string Query = """
            query ($currency: String!, $categoryId: Long) {
              shopProducts(
                currency: $currency
                filter: { categoryId: $categoryId }
                sort: PRICE_ASC
              ) { items { productId minPrice { amount currency } } nextCursor usedTypoFallback }
            }
            """;

        var response = await AdminGraphQLClient.PostForResultAsync<JsonElement>(
            client, Query, new { currency = "USD", categoryId = scenario.RootCategoryId }, Ct);

        response.Errors.Should().BeNull();

        var graphQl = response.Data!.GetProperty("shopProducts").GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("productId").GetInt64()).ToList();

        using var rest = await client.GetAsync(
            $"/api/v1/shop/products?currency=USD&categoryId={scenario.RootCategoryId}&sort=priceAsc", Ct);

        var restIds = (await rest.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("productId").GetInt64()).ToList();

        graphQl.Should().BeEquivalentTo(restIds, config => config.WithStrictOrdering(),
            "the same filter through either surface is the same shop, in the same order");
        graphQl.Should().Equal([scenario.P1, scenario.P2]);
    }

    [Fact]
    public async Task ShopProduct_Draft_Slug_Is_Not_Found()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        using var client = factory.CreateClient();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);

        var db = scope.ServiceProvider.GetRequiredService<EwaysVNext.Database.EwaysVNextDbContext>();
        var draft = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstAsync(db.Products.Where(product => product.Id == scenario.P3), Ct);

        const string Query = """
            query ($slug: String!, $currency: String!) {
              shopProduct(slug: $slug, currency: $currency) { productId }
            }
            """;

        using var response = await AdminGraphQLClient.PostAsync(
            client, Query, new { slug = draft.Slug, currency = "USD" }, Ct);

        // The code travels in extensions, so a client branches on NOT_FOUND rather than on the
        // wording of a message.
        (await AdminGraphQLClient.FirstErrorCodeAsync(response, Ct)).Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Anonymous_ShopHome_Succeeds()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        using var client = factory.CreateClient();

        _ = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);

        const string Query = """
            query { shopHome(currency: "USD") { currency newArrivals { productId } bestSellers { productId } } }
            """;

        var response = await AdminGraphQLClient.PostForResultAsync<JsonElement>(client, Query, null, Ct);

        response.Errors.Should().BeNull("no token was sent, and the storefront needs none");

        var home = response.Data!.GetProperty("shopHome");
        home.GetProperty("currency").GetString().Should().Be("USD");
        home.GetProperty("bestSellers").GetArrayLength().Should().Be(0,
            "popularity is zero until the sales tables exist");
    }

    [Fact]
    public async Task A_Bad_Price_Filter_Surfaces_As_Validation_Failed()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        const string Query = """
            query { shopProducts(currency: "USD", filter: { priceMin: "10" }) { items { productId } } }
            """;

        using var response = await AdminGraphQLClient.PostAsync(client, Query, null, Ct);

        (await AdminGraphQLClient.FirstErrorCodeAsync(response, Ct)).Should().Be("VALIDATION_FAILED",
            "a price is a two-decimal string on this surface too");
    }
}
