using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// The partner wire format, which is a published contract.
/// </summary>
/// <remarks>
/// PRD §9 specifies snake_case for the partner surface, and amounts as two-decimal strings. Both
/// are tested from raw JSON rather than through a typed client, because a typed client would
/// serialise correctly by construction and prove nothing about what a partner's own code sends.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class PartnerOrderFormatTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Body(string amountJson, string? externalOrderId = null) =>
        $$"""
        {
          "external_order_id": "{{externalOrderId ?? $"fmt-{Guid.NewGuid():N}"}}",
          "external_customer_id": "c-fmt",
          "amount": {{amountJson}},
          "currency": "USD",
          "customer": {
            "email": "fmt@example.test",
            "phone": "+6591234567",
            "full_name": "Fmt Tester"
          }
        }
        """;

    [Theory]
    [InlineData("\"500\"")]
    [InlineData("\"500.1\"")]
    [InlineData("\"500.123\"")]
    [InlineData("\"1e3\"")]
    [InlineData("\"-5.00\"")]
    [InlineData("\" 500.00\"")]
    [InlineData("500.00")]
    [InlineData("500")]
    [InlineData("null")]
    public async Task An_Amount_Must_Be_A_Two_Decimal_String(string amountJson)
    {
        using var factory = SalesApiFactory.Create(fixture);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        using var response = await client.PostAsync(
            "/api/v1/orders",
            new StringContent(Body(amountJson), Encoding.UTF8, "application/json"),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a bare number is a double to most clients, and \"500\" could mean five");

        (await CodeAsync(response)).Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task A_CamelCase_Body_Is_Refused()
    {
        using var factory = SalesApiFactory.Create(fixture);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        const string CamelCase = """
            {
              "externalOrderId": "camel-1",
              "externalCustomerId": "c-fmt",
              "amount": "500.00",
              "currency": "USD",
              "customer": { "email": "a@b.test", "phone": "+6591234567" }
            }
            """;

        using var response = await client.PostAsync(
            "/api/v1/orders", new StringContent(CamelCase, Encoding.UTF8, "application/json"), Ct);

        // The partner surface is snake_case, and silently accepting both would make the
        // published contract a suggestion.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("\"usd\"")]
    [InlineData("\"US\"")]
    [InlineData("\"USDX\"")]
    [InlineData("null")]
    public async Task A_Currency_Must_Be_Three_Upper_Case_Letters(string currencyJson)
    {
        using var factory = SalesApiFactory.Create(fixture);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var body = Body("\"500.00\"").Replace("\"USD\"", currencyJson, StringComparison.Ordinal);

        using var response = await client.PostAsync(
            "/api/v1/orders", new StringContent(body, Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("")]
    public async Task An_External_Order_Id_Must_Be_Url_And_Log_Safe(string externalOrderId)
    {
        using var factory = SalesApiFactory.Create(fixture);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        using var response = await client.PostAsync(
            "/api/v1/orders",
            new StringContent(Body("\"500.00\"", externalOrderId), Encoding.UTF8, "application/json"),
            Ct);

        // These ids end up in URLs, log lines and invoice references; one with a slash in it
        // makes every later reference ambiguous.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_Missing_Customer_Is_Refused()
    {
        using var factory = SalesApiFactory.Create(fixture);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        const string NoCustomer = """
            {
              "external_order_id": "nocust-1",
              "external_customer_id": "c-fmt",
              "amount": "500.00",
              "currency": "USD"
            }
            """;

        using var response = await client.PostAsync(
            "/api/v1/orders", new StringContent(NoCustomer, Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_Unknown_Key_Is_Unauthorized()
    {
        using var factory = SalesApiFactory.Create(fixture);
        using var client = SalesApiFactory.Client(factory, "pk_unknown_test");

        using var response = await client.PostAsync(
            "/api/v1/orders",
            new StringContent(Body("\"500.00\""), Encoding.UTF8, "application/json"),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct))
            .RootElement.GetProperty("code").GetString();
}
