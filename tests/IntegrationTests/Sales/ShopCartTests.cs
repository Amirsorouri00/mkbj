using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// The website cart over HTTP, on both surfaces.
/// </summary>
/// <remarks>
/// What is worth proving here is not that a line goes in — the aggregate and service tests cover
/// that. It is that the cart belongs to the token that asked for it, that nothing in the request
/// can set a price, and that the two API surfaces answer the same question the same way.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class ShopCartTests(PostgresFixture fixture)
{
    private const string Base = "/api/v1/shop/cart";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_Customer_Adds_Updates_And_Removes_A_Line()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);
        using var client = Shopper(factory);

        var added = await PostAsync(client, "/lines", new { variantId = shop.P1Red, quantity = 2 });
        added.StatusCode.Should().Be(HttpStatusCode.OK);

        var cart = await CartOf(added);
        cart.GetProperty("lines").GetArrayLength().Should().Be(1);
        Line(cart, 0).GetProperty("unitPrice").GetProperty("amount").GetString().Should().Be("20.00");
        cart.GetProperty("total").GetProperty("amount").GetString().Should().Be("40.00",
            "the price comes from Pricing, never from the request");

        using var updated = await client.PutAsJsonAsync(
            $"{Base}/lines/{shop.P1Red}", new { quantity = 3 }, Ct);
        (await CartOf(updated)).GetProperty("total").GetProperty("amount").GetString()
            .Should().Be("60.00");

        using var removed = await client.DeleteAsync($"{Base}/lines/{shop.P1Red}", Ct);
        (await CartOf(removed)).GetProperty("lines").GetArrayLength().Should().Be(0);

        using var fetched = await client.GetAsync(Base, Ct);
        (await CartOf(fetched)).GetProperty("lines").GetArrayLength().Should().Be(0,
            "the removal was saved, not just reported");
    }

    [Fact]
    public async Task A_Quantity_Of_A_Thousand_Is_Refused()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);
        using var client = Shopper(factory);

        using var response = await PostAsync(
            client, "/lines", new { variantId = shop.P1Red, quantity = 1_000 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task The_Cart_Belongs_To_The_Token_That_Asks_For_It()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);

        using var first = Shopper(factory);
        (await PostAsync(first, "/lines", new { variantId = shop.P1Red, quantity = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = Shopper(factory);
        using var theirs = await second.GetAsync(Base, Ct);
        (await CartOf(theirs)).GetProperty("lines").GetArrayLength().Should().Be(0,
            "a second customer must not see the first one's cart");

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(Base, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var staff = factory.CreateClient();
        staff.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateStaffToken());
        (await staff.GetAsync(Base, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a Backoffice token identifies an administrator, not a shopper");
    }

    [Fact]
    public async Task Switching_Currency_Reprices_The_Whole_Cart()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);
        using var client = Shopper(factory);

        await PostAsync(client, "/lines", new { variantId = shop.P1Red, quantity = 1 });

        using var switched = await client.PutAsJsonAsync(
            $"{Base}/currency", new { currency = "SGD" }, Ct);

        var cart = await CartOf(switched);
        cart.GetProperty("currency").GetString().Should().Be("SGD");
        Line(cart, 0).GetProperty("unitPrice").GetProperty("currency").GetString()
            .Should().Be("SGD");
        Line(cart, 0).GetProperty("unitPrice").GetProperty("amount").GetString()
            .Should().NotBe("20.00", "a converted price is not the dollar one");
    }

    [Fact]
    public async Task Rest_And_GraphQL_Show_The_Same_Cart()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);
        var token = AdminJwtFactory.CreateShopToken(Guid.NewGuid(), Email(), emailVerified: true);

        using var client = Client(factory, token);
        await PostAsync(client, "/lines", new { variantId = shop.P1Red, quantity = 2 });

        using var rest = await client.GetAsync(Base, Ct);
        var fromRest = await CartOf(rest);

        const string Query = """
            query { cart { currency total { amount currency }
                           lines { variantId sku quantity unitPrice { amount currency } } } }
            """;

        var response = await AdminGraphQLClient.PostForResultAsync<JsonElement>(
            client, Query, null, Ct);

        response.Errors.Should().BeNull();
        var fromGraph = response.Data!.GetProperty("cart");

        fromGraph.GetProperty("currency").GetString()
            .Should().Be(fromRest.GetProperty("currency").GetString());
        fromGraph.GetProperty("total").GetProperty("amount").GetString()
            .Should().Be(fromRest.GetProperty("total").GetProperty("amount").GetString());
        fromGraph.GetProperty("lines")[0].GetProperty("variantId").GetInt64()
            .Should().Be(Line(fromRest, 0).GetProperty("variantId").GetInt64());
    }

    private static async Task<StorefrontScenario> ScenarioAsync(
        WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();

        return await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);
    }

    private static string Email() => $"buyer-{Guid.NewGuid():N}@example.test";

    private static HttpClient Client(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A fresh verified shopper, so each test gets a cart of its own.</summary>
    private static HttpClient Shopper(WebApplicationFactory<Program> factory) =>
        Client(factory, AdminJwtFactory.CreateShopToken(
            Guid.NewGuid(), Email(), emailVerified: true));

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body) =>
        client.PostAsJsonAsync($"{Base}{path}", body, Ct);

    private static async Task<JsonElement> CartOf(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Every endpoint in the system answers in the same envelope, so the cart is under "data".
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("data");
    }

    private static JsonElement Line(JsonElement cart, int index) =>
        cart.GetProperty("lines")[index];

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
