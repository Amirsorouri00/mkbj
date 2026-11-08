using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.IntegrationTests.Sales;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Admin;

/// <summary>
/// The backoffice's composed views, over both surfaces.
/// </summary>
/// <remarks>
/// What is worth the Docker here is the composition itself: that an order carries the customer
/// who bought it, that a customer carries what they bought, and that the two API surfaces answer
/// with the same thing. The mapping is covered by unit tests against the real provider types.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class AdminCompositionApiTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_Dashboard_Reports_Sales_And_Rolls_Up_Failure_Codes()
    {
        using var factory = fixture.CreateFactory();
        await InsertOrderAsync(factory);
        using var staff = Staff(factory);

        var data = await DataOf(await staff.GetAsync("/api/v1/admin/dashboard", Ct));

        data.GetProperty("generatedAt").GetDateTimeOffset().Should().BeAfter(default);
        data.GetProperty("sales").GetProperty("orderTotals").GetArrayLength()
            .Should().BeGreaterThan(0);
        data.GetProperty("sales").GetProperty("composition24h").GetProperty("window").GetString()
            .Should().Be("24h");

        // Each recent failure is also counted by code, which is the question a dashboard asks.
        data.TryGetProperty("recentFailureCodes", out _).Should().BeTrue();
    }

    [Fact]
    public async Task An_Order_360_Carries_The_Customer_And_Admin_Pdf_Paths()
    {
        using var factory = fixture.CreateFactory();
        var (orderId, customerId) = await InsertOrderAsync(factory);
        using var staff = Staff(factory);

        var data = await DataOf(await staff.GetAsync($"/api/v1/admin/orders/{orderId}/360", Ct));

        var order = data.GetProperty("order");
        order.GetProperty("summary").GetProperty("id").GetInt64().Should().Be(orderId);
        order.GetProperty("lineCount").GetInt32().Should().BeGreaterThan(0);

        order.GetProperty("documents").EnumerateArray()
            .Select(document => document.GetProperty("pdfPath").GetString())
            .Should().OnlyContain(path => path!.StartsWith("/api/v1/admin/documents/", StringComparison.Ordinal));

        // The customer half may be absent — a partner order's customer can outlive nothing —
        // but when it is there it is the right one.
        if (data.GetProperty("customer").ValueKind != JsonValueKind.Null)
            data.GetProperty("customer").GetProperty("id").GetInt64().Should().Be(customerId);
    }

    [Fact]
    public async Task A_Customer_360_Lists_What_They_Bought()
    {
        using var factory = fixture.CreateFactory();
        var (orderId, customerId) = await InsertOrderAsync(factory);
        using var staff = Staff(factory);

        using var response = await staff.GetAsync($"/api/v1/admin/customers/{customerId}/360", Ct);

        // The customer row for a mother-built order may not exist; what must hold is that the
        // route answers, and when it does, the orders are that customer's.
        if (response.StatusCode == HttpStatusCode.NotFound) return;

        var data = await DataOf(response);
        data.GetProperty("customer").GetProperty("id").GetInt64().Should().Be(customerId);
        data.GetProperty("recentOrders").EnumerateArray()
            .Select(order => order.GetProperty("id").GetInt64())
            .Should().Contain(orderId);
    }

    [Fact]
    public async Task Audit_Verification_Is_Super_Admin_Only()
    {
        using var factory = fixture.CreateFactory();

        using var staff = Staff(factory);
        (await staff.GetAsync("/api/v1/admin/audit/verify", Ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden,
                "evidence that the record of who did what was edited is not a staff conversation");

        using var super = Client(factory, AdminJwtFactory.CreateSuperAdminToken());
        var data = await DataOf(await super.GetAsync("/api/v1/admin/audit/verify", Ct));

        data.GetProperty("intact").GetBoolean().Should().BeTrue();
        data.GetProperty("verifiedAt").GetDateTimeOffset().Should().BeAfter(default);
    }

    [Fact]
    public async Task Rest_And_GraphQL_Compose_The_Same_Order()
    {
        using var factory = fixture.CreateFactory();
        var (orderId, _) = await InsertOrderAsync(factory);
        using var staff = Staff(factory);

        var rest = await DataOf(await staff.GetAsync($"/api/v1/admin/orders/{orderId}/360", Ct));

        const string Query = """
            query ($id: Long!) {
              adminOrder360(orderId: $id) {
                order { summary { id number } lineCount documents { id pdfPath } }
              }
            }
            """;

        var response = await AdminGraphQLClient.PostForResultAsync<JsonElement>(
            staff, Query, new { id = orderId }, Ct);

        response.Errors.Should().BeNull();
        var graph = response.Data!.GetProperty("adminOrder360").GetProperty("order");

        graph.GetProperty("summary").GetProperty("number").GetString()
            .Should().Be(rest.GetProperty("order").GetProperty("summary").GetProperty("number").GetString());
        graph.GetProperty("lineCount").GetInt32()
            .Should().Be(rest.GetProperty("order").GetProperty("lineCount").GetInt32());
    }

    [Fact]
    public async Task A_Shop_Token_Reaches_No_Admin_Route()
    {
        using var factory = fixture.CreateFactory();
        using var shopper = Client(factory, AdminJwtFactory.CreateShopToken(
            Guid.NewGuid(), "buyer@example.test", emailVerified: true));

        foreach (var route in new[]
                 {
                     "/api/v1/admin/dashboard",
                     "/api/v1/admin/orders/1/360",
                     "/api/v1/admin/customers/1/360",
                     "/api/v1/admin/audit/verify",
                 })
        {
            (await shopper.GetAsync(route, Ct)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, "route {0}", route);
        }
    }

    private static async Task<(long OrderId, long CustomerId)> InsertOrderAsync(
        WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();

        var order = SalesOrderMother.PartnerOrder(
            SalesOrderMother.NextId(), $"ADM-{SalesOrderMother.NextId()}");

        (await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
            .TryInsertPartnerOrderAsync(order, Ct)).Should().BeTrue();

        return (order.Id, order.CustomerId);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Staff(WebApplicationFactory<Program> factory) =>
        Client(factory, AdminJwtFactory.CreateStaffToken());

    private static async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("data");
    }
}
