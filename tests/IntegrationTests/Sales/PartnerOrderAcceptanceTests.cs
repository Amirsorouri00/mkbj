using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// The PRD's partner promises, over HTTP, against the real seeded catalogue.
/// </summary>
/// <remarks>
/// A partner sends an amount and gets back a basket of real SKUs totalling it exactly, already
/// paid and invoiced. These tests are the acceptance criteria: if one fails, the product does
/// not do what it says.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class PartnerOrderAcceptanceTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Ext() => $"po-{Guid.NewGuid():N}";

    private static string Body(string externalOrderId, string amount, string customerId) =>
        $$"""
        {
          "external_order_id": "{{externalOrderId}}",
          "external_customer_id": "{{customerId}}",
          "amount": "{{amount}}",
          "currency": "USD",
          "customer": {
            "email": "{{customerId}}@example.test",
            "phone": "+6591230001",
            "full_name": "Acceptance Buyer"
          }
        }
        """;

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string externalOrderId, string amount, string customerId) =>
        client.PostAsync(
            "/api/v1/orders",
            new StringContent(Body(externalOrderId, amount, customerId), Encoding.UTF8, "application/json"),
            Ct);

    [Fact]
    public async Task Acceptance01_A_500_Usd_Order_Is_Exact_Paid_Fulfilled_And_Invoiced()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var stopwatch = Stopwatch.StartNew();
        using var response = await PostAsync(client, Ext(), "500.00", $"c-{Guid.NewGuid():N}");
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            "a partner is waiting on this call");

        var order = await DataAsync(response);

        order.GetProperty("total").GetProperty("amount").GetString().Should().Be("500.00");
        order.GetProperty("total").GetProperty("currency").GetString().Should().Be("USD");

        var lines = order.GetProperty("lines").EnumerateArray().ToList();
        lines.Should().NotBeEmpty();

        lines.Sum(line => Minor(line.GetProperty("line_total"))).Should().Be(50_000,
            "the basket must total the requested amount to the cent");

        foreach (var line in lines)
        {
            line.GetProperty("quantity").GetInt32().Should().BeGreaterThan(0);

            var unit = Minor(line.GetProperty("unit_price"));
            var reference = Minor(line.GetProperty("reference_price"));

            (100 * unit).Should().BeGreaterThanOrEqualTo(95 * reference);
            (100 * unit).Should().BeLessThanOrEqualTo(105 * reference);
        }

        var status = order.GetProperty("status");
        status.GetProperty("payment").GetString().Should().Be("Paid");
        status.GetProperty("payment_source").GetString().Should().Be("PartnerReported");
        status.GetProperty("fulfillment").GetString().Should().Be("Fulfilled");
        status.GetProperty("fulfillment_source").GetString().Should().Be("SystemAutomatic");
        status.GetProperty("fulfillment_statement").GetString().Should()
            .Be("Fulfillment is an operational assertion, not verified proof of delivery");

        var invoice = order.GetProperty("invoice");
        invoice.GetProperty("number").GetString().Should().StartWith("INV-");
        invoice.GetProperty("pdf_url").GetString().Should()
            .Be($"/api/v1/invoices/{invoice.GetProperty("id").GetInt64()}/pdf");

        var json = invoice.GetProperty("json");
        json.GetProperty("title").GetString().Should().Be("Invoice");
        json.GetProperty("taxStatement").GetString().Should().Be("Not GST registered");
    }

    [Fact]
    public async Task Acceptance02_Resubmitting_Returns_The_Same_Order_And_Invoice()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var externalOrderId = Ext();
        var customerId = $"c-{Guid.NewGuid():N}";

        using var first = await PostAsync(client, externalOrderId, "500.00", customerId);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await PostAsync(client, externalOrderId, "500.00", customerId);

        second.StatusCode.Should().Be(HttpStatusCode.OK, "a retry is not a new order");
        second.Headers.GetValues("Idempotent-Replay").Single().Should().Be("true");

        var original = await DataAsync(first);
        var replayed = await DataAsync(second);

        replayed.GetProperty("id").GetInt64().Should().Be(original.GetProperty("id").GetInt64());
        replayed.GetProperty("invoice").GetProperty("number").GetString()
            .Should().Be(original.GetProperty("invoice").GetProperty("number").GetString());
        replayed.GetProperty("lines").GetArrayLength()
            .Should().Be(original.GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task Acceptance03_The_Same_Key_For_A_Different_Amount_Is_A_Conflict()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var externalOrderId = Ext();
        var customerId = $"c-{Guid.NewGuid():N}";

        using var first = await PostAsync(client, externalOrderId, "500.00", customerId);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var conflicting = await PostAsync(client, externalOrderId, "501.00", customerId);

        conflicting.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(conflicting)).Should().Be("ORDER_IDEMPOTENCY_CONFLICT");

        // The original is untouched: a rejected second request must not disturb the first.
        using var reread = await client.GetAsync($"/api/v1/orders/ext:{externalOrderId}", Ct);
        (await DataAsync(reread)).GetProperty("total").GetProperty("amount").GetString()
            .Should().Be("500.00");
    }

    [Fact]
    public async Task Acceptance04_Partners_Are_Isolated_Even_Sharing_Every_External_Id()
    {
        using var factory = await SeededAsync();
        using var partnerA = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);
        using var partnerB = SalesApiFactory.Client(factory, SalesApiFactory.PartnerBKey);

        var externalOrderId = Ext();
        var customerId = $"c-{Guid.NewGuid():N}";

        using var aResponse = await PostAsync(partnerA, externalOrderId, "500.00", customerId);
        using var bResponse = await PostAsync(partnerB, externalOrderId, "500.00", customerId);

        aResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        bResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            "an external id is a partner's own numbering, not the shop's");

        var a = await DataAsync(aResponse);
        var b = await DataAsync(bResponse);

        b.GetProperty("id").GetInt64().Should().NotBe(a.GetProperty("id").GetInt64());
        b.GetProperty("customer_id").GetInt64().Should().NotBe(a.GetProperty("customer_id").GetInt64());

        // Partner B cannot reach any of partner A's records, and learns nothing from trying.
        using var byId = await partnerB.GetAsync($"/api/v1/orders/{a.GetProperty("id").GetInt64()}", Ct);
        byId.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(byId)).Should().Be("NOT_FOUND");

        using var byCustomer = await partnerB.GetAsync(
            $"/api/v1/customers/{a.GetProperty("customer_id").GetInt64()}/orders", Ct);
        byCustomer.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The shared external id resolves to B's own order, not A's.
        using var byExternal = await partnerB.GetAsync($"/api/v1/orders/ext:{externalOrderId}", Ct);
        (await DataAsync(byExternal)).GetProperty("id").GetInt64()
            .Should().Be(b.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Creating_A_Customer_Without_The_Scope_Is_Refused()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.NoCustomerWriteKey);

        using var response = await PostAsync(client, Ext(), "500.00", $"c-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("INSUFFICIENT_SCOPE");
    }

    [Fact]
    public async Task Listing_Filters_And_Pages()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var customerId = $"c-{Guid.NewGuid():N}";
        for (var i = 0; i < 2; i++)
        {
            using var created = await PostAsync(client, Ext(), "500.00", customerId);
            created.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var page = await client.GetAsync(
            $"/api/v1/orders?externalCustomerId={customerId}&currency=USD&limit=1", Ct);

        var data = await DataAsync(page);
        data.GetProperty("items").GetArrayLength().Should().Be(1);
        data.GetProperty("next_cursor").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_Order_Reads_The_Same_By_Id_And_By_External_Id()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var externalOrderId = Ext();
        using var created = await PostAsync(client, externalOrderId, "500.00", $"c-{Guid.NewGuid():N}");
        var order = await DataAsync(created);

        using var byId = await client.GetAsync(
            $"/api/v1/orders/{order.GetProperty("id").GetInt64()}", Ct);
        using var byExternal = await client.GetAsync($"/api/v1/orders/ext:{externalOrderId}", Ct);

        // Create, replay and both reads all reload through one query, so a partner diffing them
        // finds nothing to complain about — except the correlation id, which is per request by
        // design and belongs to the call rather than to the order.
        Comparable(await DataAsync(byExternal)).Should().Be(Comparable(await DataAsync(byId)));
    }

    [Fact]
    public async Task An_Unknown_Order_Id_Shape_Is_Not_Found()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        using var response = await client.GetAsync("/api/v1/orders/not-a-number", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<WebApplicationFactory<Program>> SeededAsync()
    {
        var factory = SalesApiFactory.Create(fixture);
        await SalesTestData.EnsureSeededAsync(factory.Services, Ct);

        return factory;
    }

    /// <summary>The order's own fields, without the per-request ones.</summary>
    private static string Comparable(JsonElement order)
    {
        var fields = order.EnumerateObject()
            .Where(property => property.Name is not ("correlation_id" or "warnings"))
            .ToDictionary(property => property.Name, property => property.Value.GetRawText(), StringComparer.Ordinal);

        return string.Join("; ", fields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static long Minor(JsonElement money) =>
        long.Parse(
            money.GetProperty("amount").GetString()!.Replace(".", string.Empty, StringComparison.Ordinal),
            System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct))
            .RootElement.GetProperty("data").Clone();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct))
            .RootElement.GetProperty("code").GetString();
}
