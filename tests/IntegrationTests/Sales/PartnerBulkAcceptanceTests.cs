using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// Acceptance 14: a hundred orders in one call, each standing or falling on its own.
/// </summary>
/// <remarks>
/// The promise is isolation. A partner uploading a day's orders must get back a verdict on every
/// one — not a batch that aborted at item ten and left it guessing which of the first nine
/// exist.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class PartnerBulkAcceptanceTests(PostgresFixture fixture)
{
    private const int Items = 100;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Acceptance14_A_Batch_Commits_Its_Successes_And_Isolates_Its_Failures()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var batch = $"bulk-{Guid.NewGuid():N}";
        var bodies = new List<string>(Items);

        for (var i = 0; i < Items; i++)
        {
            bodies.Add(i switch
            {
                // $0.05 is below the seller's minimum order value.
                10 => Item($"{batch}-10", "0.05", $"c-{batch}-10"),

                // A customer this system has never seen, with no phone number.
                20 => Item($"{batch}-20", "500.00", $"c-{batch}-20", phone: null),

                // Not a two-decimal amount.
                30 => Item($"{batch}-30", "500.1", $"c-{batch}-30"),

                // A currency the shop does not sell in.
                40 => Item($"{batch}-40", "500.00", $"c-{batch}-40", currency: "XXX"),

                // Byte-identical to item 49: a repeat inside one batch.
                50 => Item($"{batch}-49", "500.00", $"c-{batch}-49"),

                _ => Item($"{batch}-{i}", "500.00", $"c-{batch}-{i}"),
            });
        }

        using var response = await PostAsync(client, bodies);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the batch request was understood; what happened to each order is in its outcome");

        var items = (await DataAsync(response)).GetProperty("items").EnumerateArray().ToList();

        items.Should().HaveCount(Items);
        items.Select(item => item.GetProperty("index").GetInt32())
            .Should().Equal(Enumerable.Range(0, Items), "in the order submitted");

        Code(items[10]).Should().Be("AMOUNT_OUT_OF_RANGE");
        Code(items[20]).Should().Be("CUSTOMER_CONTACT_REQUIRED");
        Code(items[30]).Should().Be("VALIDATION_FAILED");
        Code(items[40]).Should().Be("CURRENCY_NOT_ENABLED");

        // 49 and 50 are the same order submitted twice. Exactly one is a creation; the other is
        // a replay or a conflict depending on which won the race — never two orders.
        var pair = new[] { items[49], items[50] };
        pair.Count(item => Status(item) == "created").Should().Be(1);

        var created = items.Where(item => Status(item) == "created").ToList();
        created.Should().HaveCountGreaterThan(90, "the four bad items must not take the rest down");

        created.Should().OnlyContain(
            item => item.GetProperty("order").GetProperty("invoice").ValueKind != JsonValueKind.Null,
            "an order that exists has been invoiced; there is no in-between state");

        // Exactly the orders the batch says it created, and no more.
        (await CountOrdersAsync(factory, batch)).Should().Be(created.Count);
    }

    [Fact]
    public async Task A_Batch_Over_The_Key_Limit_Places_Nothing()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var batch = $"over-{Guid.NewGuid():N}";
        var bodies = Enumerable.Range(0, 101)
            .Select(i => Item($"{batch}-{i}", "500.00", $"c-{batch}-{i}"))
            .ToList();

        using var response = await PostAsync(client, bodies);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("VALIDATION_FAILED");

        (await CountOrdersAsync(factory, batch)).Should().Be(0,
            "a batch refused for its size must not have placed part of itself first");
    }

    [Fact]
    public async Task A_Key_With_A_Lower_Limit_Is_Held_To_It()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerBKey);

        var batch = $"keylimit-{Guid.NewGuid():N}";
        var bodies = Enumerable.Range(0, 51)
            .Select(i => Item($"{batch}-{i}", "500.00", $"c-{batch}-{i}"))
            .ToList();

        using var response = await PostAsync(client, bodies);

        // Partner B's key allows 50. The limit belongs to the key, not to the request.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountOrdersAsync(factory, batch)).Should().Be(0);
    }

    [Fact]
    public async Task An_Empty_Batch_Is_Refused()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        using var response = await PostAsync(client, []);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("VALIDATION_FAILED");
    }

    private static string Item(
        string externalOrderId,
        string amount,
        string customerId,
        string? phone = "+6591110002",
        string currency = "USD")
    {
        var contact = phone is null
            ? $"\"email\": \"{customerId}@example.test\""
            : $"\"email\": \"{customerId}@example.test\", \"phone\": \"{phone}\"";

        return $$"""
            {
              "external_order_id": "{{externalOrderId}}",
              "external_customer_id": "{{customerId}}",
              "amount": "{{amount}}",
              "currency": "{{currency}}",
              "customer": { {{contact}} }
            }
            """;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, IReadOnlyList<string> items) =>
        client.PostAsync(
            "/api/v1/orders/bulk",
            new StringContent(
                $$"""{ "items": [{{string.Join(",", items)}}] }""",
                Encoding.UTF8,
                "application/json"),
            Ct);

    private async Task<WebApplicationFactory<Program>> SeededAsync()
    {
        var factory = SalesApiFactory.Create(fixture);
        await SalesTestData.EnsureSeededAsync(factory.Services, Ct);

        return factory;
    }

    private static async Task<long> CountOrdersAsync(
        WebApplicationFactory<Program> factory, string batch)
    {
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
            .Orders.CountAsync(o => o.ExternalOrderId!.StartsWith(batch), Ct);
    }

    private static string? Status(JsonElement item) => item.GetProperty("status").GetString();

    private static string? Code(JsonElement item) =>
        item.GetProperty("error").GetProperty("code").GetString();

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct))
            .RootElement.GetProperty("data").Clone();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct))
            .RootElement.GetProperty("code").GetString();
}
