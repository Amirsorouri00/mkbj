using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// A partner's invoices over HTTP, and the one thing they must not be able to do: block a sale.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class PartnerInvoiceTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_Pdf_Route_Redirects_To_A_Short_Lived_Link()
    {
        using var factory = await SeededAsync();
        using var client = NoRedirects(factory, SalesApiFactory.PartnerAKey);

        var order = await PlaceAsync(client);
        var invoiceId = order.GetProperty("invoice").GetProperty("id").GetInt64();

        using var response = await client.GetAsync($"/api/v1/invoices/{invoiceId}/pdf", Ct);

        // Found, not Moved Permanently: the link expires in minutes, and a cached permanent
        // redirect would send a partner to a dead URL forever.
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task The_Pdf_Is_Rendered_Once_And_Stored_Under_The_Orders_Key()
    {
        using var factory = await SeededAsync();
        using var client = NoRedirects(factory, SalesApiFactory.PartnerAKey);

        var order = await PlaceAsync(client);
        var orderId = order.GetProperty("id").GetInt64();
        var invoice = order.GetProperty("invoice");
        var number = invoice.GetProperty("number").GetString();

        using (var first = await client.GetAsync(
            $"/api/v1/invoices/{invoice.GetProperty("id").GetInt64()}/pdf", Ct))
        {
            first.StatusCode.Should().Be(HttpStatusCode.Redirect);
        }

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

        (await store.ExistsAsync($"{orderId}/{number}.pdf", Ct)).Should().BeTrue(
            "the file an order points at is the file that gets written");
    }

    [Fact]
    public async Task Another_Partners_Invoice_Is_Not_Found()
    {
        using var factory = await SeededAsync();
        using var partnerA = NoRedirects(factory, SalesApiFactory.PartnerAKey);
        using var partnerB = NoRedirects(factory, SalesApiFactory.PartnerBKey);

        var order = await PlaceAsync(partnerA);
        var invoiceId = order.GetProperty("invoice").GetProperty("id").GetInt64();

        using var document = await partnerB.GetAsync($"/api/v1/invoices/{invoiceId}", Ct);
        document.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var pdf = await partnerB.GetAsync($"/api/v1/invoices/{invoiceId}/pdf", Ct);
        pdf.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "and the PDF route must not be the way around the document route");
    }

    [Fact]
    public async Task Searching_Answers_For_Found_And_Unknown_Ids_In_Order()
    {
        using var factory = await SeededAsync();
        using var client = NoRedirects(factory, SalesApiFactory.PartnerAKey);

        var externalOrderId = $"inv-{Guid.NewGuid():N}";
        await PlaceAsync(client, externalOrderId);

        using var response = await client.PostAsync(
            "/api/v1/invoices/search",
            new StringContent(
                $$"""{ "external_order_ids": ["{{externalOrderId}}", "never-heard-of-it"] }""",
                Encoding.UTF8,
                "application/json"),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = (await DataAsync(response)).GetProperty("items").EnumerateArray().ToList();

        items.Should().HaveCount(2);
        items[0].GetProperty("status").GetString().Should().Be("found");
        items[0].GetProperty("invoices").GetArrayLength().Should().Be(1);
        items[1].GetProperty("status").GetString().Should().Be("not_found");
        items[1].GetProperty("invoices").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Searching_Too_Many_Ids_Is_Refused()
    {
        using var factory = await SeededAsync();
        using var client = NoRedirects(factory, SalesApiFactory.PartnerAKey);

        var ids = string.Join(",", Enumerable.Range(0, 101).Select(i => $"\"po-{i}\""));

        using var response = await client.PostAsync(
            "/api/v1/invoices/search",
            new StringContent($$"""{ "external_order_ids": [{{ids}}] }""", Encoding.UTF8, "application/json"),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Object storage being down must not stop a sale.
    /// </summary>
    /// <remarks>
    /// This is why rendering is lazy. If an invoice were produced during order creation, MinIO
    /// would sit on the path of every partner order — and a partner already debited on its own
    /// side would be told its order failed because a file could not be written.
    /// </remarks>
    [Fact]
    public async Task A_Broken_Document_Store_Still_Lets_An_Order_Be_Placed()
    {
        using var factory = await SeededAsync(services =>
        {
            services.RemoveAll<IDocumentStore>();
            services.AddScoped<IDocumentStore, BrokenDocumentStore>();
        });

        using var client = NoRedirects(factory, SalesApiFactory.PartnerAKey);

        var order = await PlaceAsync(client);

        order.GetProperty("invoice").GetProperty("number").GetString().Should().StartWith("INV-");

        // The download is what breaks, and only the download.
        using var pdf = await client.GetAsync(
            $"/api/v1/invoices/{order.GetProperty("invoice").GetProperty("id").GetInt64()}/pdf", Ct);

        pdf.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    private sealed class BrokenDocumentStore : IDocumentStore
    {
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            throw new InvalidOperationException("Object storage is unreachable.");

        public Task PutPdfAsync(string key, byte[] pdf, CancellationToken ct = default) =>
            throw new InvalidOperationException("Object storage is unreachable.");

        public Task<Uri> GetDownloadUrlAsync(string key, CancellationToken ct = default) =>
            throw new InvalidOperationException("Object storage is unreachable.");
    }

    private async Task<WebApplicationFactory<Program>> SeededAsync(
        Action<IServiceCollection>? configure = null)
    {
        var factory = SalesApiFactory.Create(fixture, configure);
        await SalesTestData.EnsureSeededAsync(factory.Services, Ct);

        return factory;
    }

    private static HttpClient NoRedirects(WebApplicationFactory<Program> factory, string key)
    {
        // The redirect is the behaviour under test; following it would hide the status code and
        // send the test at a presigned URL instead.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

        return client;
    }

    private static async Task<JsonElement> PlaceAsync(HttpClient client, string? externalOrderId = null)
    {
        var customerId = $"c-{Guid.NewGuid():N}";

        using var response = await client.PostAsync(
            "/api/v1/orders",
            new StringContent(
                $$"""
                {
                  "external_order_id": "{{externalOrderId ?? $"inv-{Guid.NewGuid():N}"}}",
                  "external_customer_id": "{{customerId}}",
                  "amount": "500.00",
                  "currency": "USD",
                  "customer": { "email": "{{customerId}}@example.test", "phone": "+6591110003" }
                }
                """,
                Encoding.UTF8,
                "application/json"),
            Ct);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));

        return await DataAsync(response);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct))
            .RootElement.GetProperty("data").Clone();
}
