using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.IntegrationTests.Support;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// Checkout: an order at the price the customer was shown, or no order at all.
/// </summary>
/// <remarks>
/// The refusal path is the one worth the Docker. A price that moved between looking and buying
/// must leave <b>nothing</b> behind — no order, no invoice number drawn from a gapless sequence
/// — and the customer must be handed the basket as it now stands rather than an error they have
/// to interpret.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class ShopCheckoutTests(PostgresFixture fixture)
{
    private const string Cart = "/api/v1/shop/cart";
    private const string Checkout = "/api/v1/shop/checkout";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Acceptance09_Checkout_Is_Pro_Forma_Until_An_Admin_Confirms()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);
        using var client = Shopper(factory);

        await AddAsync(client, shop.P1Red, 2);
        var total = await TotalAsync(client);

        using var placed = await client.PostAsJsonAsync(
            Checkout, new { expectedTotal = new { amount = total, currency = "USD" } }, Ct);

        placed.StatusCode.Should().Be(HttpStatusCode.Created);

        var order = (await placed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("data");

        order.GetProperty("status").GetProperty("order").GetString().Should().Be("PendingPayment");
        order.GetProperty("status").GetProperty("payment").GetString().Should().Be("Unpaid");
        order.GetProperty("status").GetProperty("documents").GetString().Should().Be("ProFormaIssued");

        var documents = order.GetProperty("documents").EnumerateArray().ToList();
        documents.Should().ContainSingle("nothing is invoiced until someone has seen the money");
        documents[0].GetProperty("type").GetString().Should().Be("ProForma");
        documents[0].GetProperty("number").GetString().Should().StartWith("PF-");

        // The instruction the buyer must pay against travels on the document itself.
        documents[0].GetProperty("content").GetProperty("paymentInstruction")
            .GetProperty("text").GetString().Should().NotBeNullOrWhiteSpace();

        using var emptied = await client.GetAsync(Cart, Ct);
        (await DataOf(emptied)).GetProperty("lines").GetArrayLength().Should().Be(0,
            "the cart became the order");

        using var history = await client.GetAsync("/api/v1/shop/orders", Ct);
        (await DataOf(history)).GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task A_Deactivated_Line_Is_A_Price_Change_And_Writes_Nothing()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);
        using var client = Shopper(factory);

        await AddAsync(client, shop.P1Red, 1);
        await AddAsync(client, shop.P1Blue, 1);
        var total = await TotalAsync(client);

        await DeactivateAsync(factory, shop.P1, shop.P1Blue);

        using var refused = await client.PostAsJsonAsync(
            Checkout, new { expectedTotal = new { amount = total, currency = "USD" } }, Ct);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await refused.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("code").GetString().Should().Be("PRICE_CHANGED");

        var cart = body.GetProperty("details").GetProperty("cart");
        cart.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("variantId").GetInt64())
            .Should().Equal(shop.P1Red);
        cart.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("kind").GetString())
            .Should().Equal("removed");

        using var history = await client.GetAsync("/api/v1/shop/orders", Ct);
        (await DataOf(history)).GetProperty("items").GetArrayLength().Should().Be(0,
            "a refused checkout leaves no order behind");

        // And the second attempt, at the total the refusal just showed, goes through.
        var fresh = await TotalAsync(client);
        using var placed = await client.PostAsJsonAsync(
            Checkout, new { expectedTotal = new { amount = fresh, currency = "USD" } }, Ct);

        placed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_Stale_Expected_Total_Is_Refused()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);
        using var client = Shopper(factory);

        await AddAsync(client, shop.P1Red, 1);

        using var refused = await client.PostAsJsonAsync(
            Checkout, new { expectedTotal = new { amount = "19.99", currency = "USD" } }, Ct);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(refused)).Should().Be("PRICE_CHANGED");
    }

    [Fact]
    public async Task An_Unverified_Email_Cannot_Check_Out()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);

        var identity = Guid.NewGuid();
        var email = $"buyer-{Guid.NewGuid():N}@example.test";

        using var verified = Client(factory, AdminJwtFactory.CreateShopToken(identity, email, true));
        await AddAsync(verified, shop.P1Red, 1);
        var total = await TotalAsync(verified);

        // The same person, now presenting a token that says their address is unconfirmed.
        using var unverified = Client(factory, AdminJwtFactory.CreateShopToken(identity, email, false));

        using var refused = await unverified.PostAsJsonAsync(
            Checkout, new { expectedTotal = new { amount = total, currency = "USD" } }, Ct);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOf(refused)).Should().Be("EMAIL_NOT_VERIFIED");

        using var history = await verified.GetAsync("/api/v1/shop/orders", Ct);
        (await DataOf(history)).GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Another_Customers_Order_Is_Not_Found()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);

        using var mine = Shopper(factory);
        await AddAsync(mine, shop.P1Red, 1);
        var total = await TotalAsync(mine);

        using var placed = await mine.PostAsJsonAsync(
            Checkout, new { expectedTotal = new { amount = total, currency = "USD" } }, Ct);

        var orderId = (await DataOf(placed, HttpStatusCode.Created)).GetProperty("id").GetInt64();

        using var theirs = Shopper(factory);
        using var response = await theirs.GetAsync($"/api/v1/shop/orders/{orderId}", Ct);

        // Not "forbidden": that answer confirms the order exists, which is not their business.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<StorefrontScenario> ScenarioAsync(
        WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();

        return await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);
    }

    private static async Task DeactivateAsync(
        WebApplicationFactory<Program> factory, long productId, long variantId)
    {
        using var scope = factory.Services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ICatalogAdminService>()
            .SetVariantActiveAsync(
                productId, variantId, active: false,
                new AdminActor(Guid.NewGuid(), IsSuperAdmin: true), Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Shopper(WebApplicationFactory<Program> factory) =>
        Client(factory, AdminJwtFactory.CreateShopToken(
            Guid.NewGuid(), $"buyer-{Guid.NewGuid():N}@example.test", emailVerified: true));

    private static async Task AddAsync(HttpClient client, long variantId, int quantity)
    {
        using var response = await client.PostAsJsonAsync(
            $"{Cart}/lines", new { variantId, quantity }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<string> TotalAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Cart, Ct);

        return (await DataOf(response)).GetProperty("total").GetProperty("amount").GetString()!;
    }

    private static async Task<JsonElement> DataOf(
        HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("data");
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
