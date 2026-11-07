using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// The partner flow under the conditions that actually go wrong.
/// </summary>
/// <remarks>
/// Retries racing each other, a catalogue that moves between composing and selling, a customer
/// service that is down, a composition that cannot succeed. Each of these leaves a trace in the
/// database, and what matters is that the trace is <b>whole or absent</b> — never half an order.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class PartnerOrderFlowTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Ext() => $"flow-{Guid.NewGuid():N}";

    private static string Body(
        string externalOrderId,
        string amount,
        string customerId,
        string? email = null,
        string? phone = "+6591110001",
        string currency = "USD")
    {
        var contact = new List<string>();
        if (email is not null) contact.Add($"\"email\": \"{email}\"");
        if (phone is not null) contact.Add($"\"phone\": \"{phone}\"");

        return $$"""
            {
              "external_order_id": "{{externalOrderId}}",
              "external_customer_id": "{{customerId}}",
              "amount": "{{amount}}",
              "currency": "{{currency}}",
              "customer": { {{string.Join(", ", contact)}} }
            }
            """;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string body) =>
        client.PostAsync(
            "/api/v1/orders", new StringContent(body, Encoding.UTF8, "application/json"), Ct);

    [Fact]
    public async Task Acceptance05_A_Contact_Change_Is_Audited_And_An_Omitted_Field_Is_Kept()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var customerId = $"c-{Guid.NewGuid():N}";

        using var first = await PostAsync(
            client, Body(Ext(), "500.00", customerId, "e1@example.test"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second order changes only the email. The phone was not sent, so it must survive.
        using var second = await PostAsync(
            client, Body(Ext(), "500.00", customerId, "e2@example.test", phone: null));

        second.StatusCode.Should().Be(HttpStatusCode.Created);
        (await DataAsync(second)).GetProperty("warnings").GetArrayLength().Should().Be(0);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        var customer = await db.Customers
            .Include(c => c.ContactChanges)
            .FirstAsync(c => c.ExternalCustomerId == customerId, Ct);

        customer.Email.Should().Be("e2@example.test");
        customer.Phone.Should().Be("+6591110001", "an omitted field is not a cleared field");

        var change = customer.ContactChanges.Should().ContainSingle().Which;
        change.OldValue.Should().Be("e1@example.test");
        change.NewValue.Should().Be("e2@example.test");
        change.SourcePartnerId.Should().Be(SalesApiFactory.PartnerAId,
            "an audit of who changed a customer's details is the point of recording them");

        // The invoice bills the address this order was placed with.
        (await DataAsync(second)).GetProperty("invoice").GetProperty("json")
            .GetProperty("buyer").GetProperty("email").GetString()
            .Should().Be("e2@example.test");
    }

    [Fact]
    public async Task Acceptance06_A_New_Customer_Without_A_Phone_Writes_Nothing()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var customerId = $"c-{Guid.NewGuid():N}";
        var before = await CountsAsync(factory);

        using var response = await PostAsync(
            client, Body(Ext(), "500.00", customerId, "e@example.test", phone: null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("CUSTOMER_CONTACT_REQUIRED");

        (await CountsAsync(factory)).Should().Be(before,
            "a refused order must leave nothing behind for the retry to trip over");

        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
            .Customers.AnyAsync(c => c.ExternalCustomerId == customerId, Ct))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Acceptance07a_A_Composition_Timeout_Writes_Nothing_But_An_Attempt()
    {
        using var factory = await SeededAsync(SalesApiFactory.WithNoCompositionTime);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var customerId = $"c-{Guid.NewGuid():N}";
        var before = await CountsAsync(factory);

        using var response = await PostAsync(
            client, Body(Ext(), "500.00", customerId, "e@example.test"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(response)).Should().Be("COMPOSITION_TIMEOUT");
        (await BodyAsync(response)).GetProperty("retryable").GetBoolean().Should().BeTrue();

        (await CountsAsync(factory)).Should().Be(before);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        (await db.Customers.AnyAsync(c => c.ExternalCustomerId == customerId, Ct))
            .Should().BeFalse("nothing is written until a basket exists");

        // The attempt row is the only trace, which is what it is for.
        (await db.CompositionAttempts
            .AnyAsync(a => !a.Succeeded && a.ErrorCode == "COMPOSITION_TIMEOUT", Ct))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Acceptance07b_An_Impossible_Amount_Writes_Nothing_But_An_Attempt()
    {
        var (variant, target) = await ImpossibleProbeAsync();
        using var factory = await SeededAsync(SalesApiFactory.WithPoolLimitedTo(variant.VariantId));
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var before = await CountsAsync(factory);

        using var response = await PostAsync(
            client,
            Body(Ext(), Format(target), $"c-{Guid.NewGuid():N}", "e@example.test"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("EXACT_COMPOSITION_UNAVAILABLE");
        (await BodyAsync(response)).GetProperty("retryable").GetBoolean().Should().BeFalse(
            "the same amount against the same catalogue will fail the same way");

        (await CountsAsync(factory)).Should().Be(before);
    }

    [Fact]
    public async Task No_Stored_Line_Has_Ever_Breached_Its_Band()
    {
        using var factory = await SeededAsync();
        using var scope = factory.Services.CreateScope();

        // The database's own check constraint forbids this, so a row here would mean the
        // constraint is missing — which is a different failure from the aggregate letting one
        // through, and worth catching separately.
        var breaches = await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
            .Database.SqlQueryRaw<long>(
                """
                SELECT count(*) AS "Value" FROM sales.order_lines
                WHERE band_enforced
                  AND (unit_price_minor * 100 < reference_price_minor * 95
                       OR unit_price_minor * 100 > reference_price_minor * 105)
                """)
            .SingleAsync(Ct);

        breaches.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_Retries_Of_One_Request_Create_Exactly_One_Order()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var externalOrderId = Ext();
        var body = Body(externalOrderId, "500.00", $"c-{externalOrderId}", "conc@example.test");

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => PostAsync(client, body)));

        try
        {
            responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);

            var replays = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
            replays.Should().HaveCount(9);
            replays.Should().OnlyContain(
                r => r.Headers.GetValues("Idempotent-Replay").Single() == "true");

            var ids = new List<long>();
            foreach (var response in responses) ids.Add((await DataAsync(response)).GetProperty("id").GetInt64());

            ids.Distinct().Should().ContainSingle("ten retries are one order");
            (await CountOrdersAsync(factory, externalOrderId)).Should().Be(1);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Concurrent_Different_Amounts_Under_One_Key_Create_One_And_Refuse_The_Rest()
    {
        using var factory = await SeededAsync();
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var externalOrderId = Ext();
        var customerId = $"c-{externalOrderId}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            PostAsync(client, Body(
                externalOrderId, $"{500 + i}.00", customerId, "conc2@example.test"))));

        try
        {
            responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
            responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);

            foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            {
                (await CodeAsync(conflict)).Should().Be("ORDER_IDEMPOTENCY_CONFLICT");
            }

            (await CountOrdersAsync(factory, externalOrderId)).Should().Be(1);

            // One customer, not ten: the creation race resolves inside Customer rather than
            // leaving nine orphans behind.
            using var scope = factory.Services.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
                .Customers.CountAsync(c => c.ExternalCustomerId == customerId, Ct))
                .Should().Be(1);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task A_Contact_Sync_Failure_Still_Returns_A_Created_Order()
    {
        using var factory = await SeededAsync(SalesApiFactory.WithFailingContactSync);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        var customerId = $"c-{Guid.NewGuid():N}";

        using var first = await PostAsync(
            client, Body(Ext(), "500.00", customerId, "sync1@example.test"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await PostAsync(
            client, Body(Ext(), "500.00", customerId, "sync2@example.test"));

        second.StatusCode.Should().Be(HttpStatusCode.Created,
            "the order is sold, paid and invoiced; a phone book that would not update is not a "
            + "reason to refuse money");

        var order = await DataAsync(second);
        order.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().Equal("CONTACT_SYNC_FAILED");

        // The invoice still bills what this order was placed with, sync or no sync.
        order.GetProperty("invoice").GetProperty("json").GetProperty("buyer")
            .GetProperty("email").GetString().Should().Be("sync2@example.test");
    }

    [Fact]
    public async Task A_Variant_Withdrawn_After_The_Pool_Was_Built_Is_Never_Sold()
    {
        var variant = await AnyUsdVariantAsync();

        StaleOncePoolGateway.Reset();
        using var factory = await SeededAsync(SalesApiFactory.WithStaleFirstPool);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);

        // Warm the stale pool, then withdraw the variant it still believes in.
        using (var warm = factory.Services.CreateScope())
        {
            await warm.ServiceProvider.GetRequiredService<ICandidatePoolGateway>()
                .GetAsync("USD", DateTimeOffset.UtcNow, Ct);
        }

        await DeactivateAsync(factory, variant.VariantId);

        using var response = await PostAsync(
            client, Body(Ext(), "500.00", $"c-{Guid.NewGuid():N}", "stale@example.test"));

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the live re-quote drops the withdrawn variant and the basket is composed again");

        (await DataAsync(response)).GetProperty("lines").EnumerateArray()
            .Select(line => line.GetProperty("variant_id").GetInt64())
            .Should().NotContain(variant.VariantId,
                "a product that was withdrawn before the sale must not be on the invoice");
    }

    private async Task<WebApplicationFactory<Program>> SeededAsync(
        Action<IServiceCollection>? configure = null)
    {
        var factory = SalesApiFactory.Create(fixture, configure);
        await SalesTestData.EnsureSeededAsync(factory.Services, Ct);

        return factory;
    }

    private sealed record VariantBand(long VariantId, long HiMinor);

    /// <summary>One sellable, USD-priced variant from the seeded catalogue.</summary>
    private async Task<VariantBand> AnyUsdVariantAsync()
    {
        var candidate = (await CandidatesAsync())
            .OrderByDescending(c => c.ReferenceMinor).ThenBy(c => c.VariantId)
            .First();

        return new VariantBand(candidate.VariantId, candidate.HiMinor);
    }

    /// <summary>
    /// A variant and an amount that no basket of <b>only that variant</b> can total, which the
    /// seller's current limits still permit.
    /// </summary>
    /// <remarks>
    /// The variant is chosen against the live limits rather than assumed. Those limits are
    /// seller configuration that other tests in this shared database change, and a probe outside
    /// them refuses with <c>AMOUNT_OUT_OF_RANGE</c> — still a refusal, so the test would read as
    /// evidence for a rule it never reached. Twice already it did exactly that.
    /// </remarks>
    private async Task<(VariantBand Variant, long Target)> ImpossibleProbeAsync()
    {
        using var factory = SalesApiFactory.Create(fixture);
        await SalesTestData.EnsureSeededAsync(factory.Services, Ct);

        using var scope = factory.Services.CreateScope();
        var limits = await scope.ServiceProvider.GetRequiredService<ISalesSettingsGateway>()
            .GetCommerceLimitsAsync(Ct);

        const int Units = 3;

        var candidate = (await CandidatesAsync())
            .Where(c => (Units * c.HiMinor) + 1 >= limits.MinUsdMinor
                && (Units * c.HiMinor) + 1 <= limits.MaxUsdMinor)
            .OrderByDescending(c => c.ReferenceMinor).ThenBy(c => c.VariantId)
            .FirstOrDefault();

        candidate.Should().NotBeNull(
            "the catalogue must hold at least one variant whose band fits inside the seller's "
            + "order limits, or no partner order is possible at all");

        return (
            new VariantBand(candidate!.VariantId, candidate.HiMinor),
            (Units * candidate.HiMinor) + 1);
    }

    private async Task<IReadOnlyList<EwaysVNext.Modules.Sales.Application.Composition.CompositionCandidate>>
        CandidatesAsync()
    {
        using var factory = SalesApiFactory.Create(fixture);
        await SalesTestData.EnsureSeededAsync(factory.Services, Ct);

        using var scope = factory.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<ICandidatePoolGateway>()
            .GetAsync("USD", DateTimeOffset.UtcNow, Ct)).Value!.Candidates;
    }

    private static async Task DeactivateAsync(
        WebApplicationFactory<Program> factory, long variantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE catalog.variants SET is_active = false WHERE id = {0}", [variantId], Ct);

        // Sellability is versioned, and the candidate cache watches that version.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT nextval('catalog.sellability_version')", Ct);
    }

    private sealed record Counts(long Orders, long Lines, long Payments, long Documents);

    private static async Task<Counts> CountsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        // Whole statements, not a table name interpolated into one. The analyser is right to
        // object even here: an interpolated identifier is the shape an injection takes, and a
        // test is where that habit gets learned.
        async Task<long> CountAsync(string sql) =>
            await db.Database.SqlQueryRaw<long>(sql).SingleAsync(Ct);

        return new Counts(
            await CountAsync("SELECT count(*) AS \"Value\" FROM sales.orders"),
            await CountAsync("SELECT count(*) AS \"Value\" FROM sales.order_lines"),
            await CountAsync("SELECT count(*) AS \"Value\" FROM sales.order_payments"),
            await CountAsync("SELECT count(*) AS \"Value\" FROM sales.order_documents"));
    }

    private static async Task<long> CountOrdersAsync(
        WebApplicationFactory<Program> factory, string externalOrderId)
    {
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
            .Orders.CountAsync(o => o.ExternalOrderId == externalOrderId, Ct);
    }

    private static string Format(long minor) =>
        EwaysVNext.Modules.Sales.Application.Money.AmountText.Format(minor);

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("data").Clone();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("code").GetString();
}
