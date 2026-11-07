using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// PRD acceptance 9 and 12: the website order's life, and money going back.
/// </summary>
/// <remarks>
/// Both run over HTTP against the real database, because what they assert is not domain logic —
/// the aggregate tests cover that — but that the gates, the audit rows and the two channels'
/// histories behave as a shop operator would expect.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class WebsiteOrderAcceptanceTests(PostgresFixture fixture)
{
    private const string Cart = "/api/v1/shop/cart";
    private const string Checkout = "/api/v1/shop/checkout";
    private const string Admin = "/api/v1/admin/orders";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Acceptance09_Website_Checkout_Is_Pro_Forma_Until_Admin_Confirms()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);
        using var buyer = Shopper(factory);
        using var staff = Staff(factory);

        var orderId = await PlaceAsync(buyer, shop.P1Red, 2);

        using var confirmed = await staff.PostAsJsonAsync(
            $"{Admin}/{orderId}/confirm-payment",
            new { method = "bank-transfer", reference = "TT-1" }, Ct);

        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await OrderAsync(buyer, orderId);
        var status = order.GetProperty("status");
        status.GetProperty("order").GetString().Should().Be("Completed");
        status.GetProperty("payment").GetString().Should().Be("Paid");
        status.GetProperty("paymentSource").GetString().Should().Be("AdminConfirmed");
        status.GetProperty("fulfillment").GetString().Should().Be("Fulfilled");
        status.GetProperty("documents").GetString().Should().Be("Invoiced");

        var documents = order.GetProperty("documents").EnumerateArray().ToList();
        documents.Select(d => d.GetProperty("type").GetString())
            .Should().Equal("ProForma", "Commercial");
        documents[1].GetProperty("content").GetProperty("title").GetString()
            .Should().Be("Invoice", "this seller is not GST registered");

        // Confirming twice is a conflict, not a second invoice.
        using var again = await staff.PostAsJsonAsync(
            $"{Admin}/{orderId}/confirm-payment", new { method = "bank-transfer" }, Ct);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(again)).Should().Be("ORDER_STATE_CONFLICT");

        using var cancelPaid = await staff.PostAsJsonAsync($"{Admin}/{orderId}/cancel", new { }, Ct);
        cancelPaid.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "something paid for is refunded, not cancelled");

        // An unpaid one cancels.
        var second = await PlaceAsync(buyer, shop.P1Blue, 1);
        using var cancelled = await staff.PostAsJsonAsync($"{Admin}/{second}/cancel", new { }, Ct);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);

        (await OrderAsync(buyer, second)).GetProperty("status").GetProperty("order").GetString()
            .Should().Be("Cancelled");
    }

    [Fact]
    public async Task Acceptance12_Refunds_Credit_Notes_Are_Linked_Audited_And_Super_Only()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);
        using var buyer = Shopper(factory);
        using var staff = Staff(factory);
        using var super = Super(factory);

        var orderId = await PlaceAsync(buyer, shop.P1Red, 2);
        await staff.PostAsJsonAsync(
            $"{Admin}/{orderId}/confirm-payment", new { method = "bank-transfer" }, Ct);

        var order = await OrderAsync(buyer, orderId);
        var invoice = order.GetProperty("documents").EnumerateArray()
            .Single(d => d.GetProperty("type").GetString() == "Commercial");

        var invoiceId = invoice.GetProperty("id").GetInt64();
        var invoiceContent = invoice.GetProperty("content").GetRawText();

        using var staffAttempt = await staff.PostAsJsonAsync(
            $"{Admin}/{orderId}/refunds", new { amount = "10.00", reason = "Nope" }, Ct);

        staffAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "money leaving the business is a super admin's decision");

        using var partial = await super.PostAsJsonAsync(
            $"{Admin}/{orderId}/refunds",
            new { amount = "10.00", reason = "Damaged", externalReference = "RTN-1" }, Ct);

        partial.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await partial.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("data");
        result.GetProperty("paymentStatus").GetString().Should().Be("PartiallyRefunded");
        result.GetProperty("creditNoteNumber").GetString().Should().StartWith("CN-");
        result.GetProperty("refundedMinor").GetInt64().Should().Be(1_000);

        using var rest = await super.PostAsJsonAsync(
            $"{Admin}/{orderId}/refunds", new { amount = "30.00", reason = "Rest" }, Ct);

        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rest.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("data").GetProperty("paymentStatus").GetString().Should().Be("Refunded");

        using var tooMuch = await super.PostAsJsonAsync(
            $"{Admin}/{orderId}/refunds", new { amount = "0.01", reason = "One more" }, Ct);

        tooMuch.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
        (await CodeOf(tooMuch)).Should().Be("REFUND_EXCEEDS_REFUNDABLE");

        var after = await OrderAsync(buyer, orderId);
        var creditNotes = after.GetProperty("documents").EnumerateArray()
            .Where(d => d.GetProperty("type").GetString() == "CreditNote").ToList();

        creditNotes.Should().HaveCount(2);
        creditNotes.Should().OnlyContain(
            note => note.GetProperty("relatedDocumentId").GetInt64() == invoiceId);

        after.GetProperty("documents").EnumerateArray()
            .Single(d => d.GetProperty("type").GetString() == "Commercial")
            .GetProperty("content").GetRawText()
            .Should().Be(invoiceContent, "an issued invoice is never edited");

        (await AuditCountAsync(orderId)).Should().Be(2,
            "each refund is one audited act, by name and by the admin who made it");
    }

    private static async Task<StorefrontScenario> ScenarioAsync(
        WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();

        return await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);
    }

    /// <summary>Adds one variant and checks out at whatever the cart says it costs.</summary>
    private static async Task<long> PlaceAsync(HttpClient buyer, long variantId, int quantity)
    {
        using var added = await buyer.PostAsJsonAsync(
            $"{Cart}/lines", new { variantId, quantity }, Ct);

        added.StatusCode.Should().Be(HttpStatusCode.OK);

        using var cart = await buyer.GetAsync(Cart, Ct);
        var total = (await DataOf(cart)).GetProperty("total").GetProperty("amount").GetString();

        using var placed = await buyer.PostAsJsonAsync(
            Checkout, new { expectedTotal = new { amount = total, currency = "USD" } }, Ct);

        return (await DataOf(placed, HttpStatusCode.Created)).GetProperty("id").GetInt64();
    }

    private static async Task<JsonElement> OrderAsync(HttpClient buyer, long orderId)
    {
        using var response = await buyer.GetAsync($"/api/v1/shop/orders/{orderId}", Ct);

        return await DataOf(response);
    }

    private async Task<int> AuditCountAsync(long orderId)
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync(Ct);

        await using var command = new NpgsqlCommand(
            "SELECT count(*)::int FROM audit.audit_events "
            + "WHERE action = 'sales.order.refund' AND target_id = @id", connection);

        command.Parameters.AddWithValue(
            "id", orderId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return (int)(await command.ExecuteScalarAsync(Ct))!;
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

    private static HttpClient Staff(WebApplicationFactory<Program> factory) =>
        Client(factory, AdminJwtFactory.CreateStaffToken());

    private static HttpClient Super(WebApplicationFactory<Program> factory) =>
        Client(factory, AdminJwtFactory.CreateSuperAdminToken());

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
