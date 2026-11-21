using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Mkbj.Database;
using Mkbj.Modules.Sales.QueryApplication.Admin;
using Mkbj.Modules.Sales.Application.Domain.Telemetry;
using Mkbj.Modules.Sales.Application.Ports.Outbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mkbj.IntegrationTests.Sales;

[Collection(PostgresSuite.Name)]
public sealed class PartnerRequestJournalTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Input_And_Exact_Response_Are_Durable_And_Replays_Link_The_Same_Invoice()
    {
        using var factory = SalesApiFactory.Create(fixture);
        await SalesTestData.EnsureSeededAsync(factory.Services, Ct);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);
        var externalId = $"journal-{Guid.NewGuid():N}";
        var body = JsonSerializer.Serialize(new { external_order_id = externalId, external_customer_id = externalId,
            amount = "500.00", currency = "USD", customer = new { email = "journal@example.test", phone = "+6591234567", full_name = "Journal Buyer" } });
        async Task<HttpResponseMessage> Send() => await client.PostAsync("/api/v1/orders", new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        using var first = await Send();
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(Ct));
        using var replay = await Send();
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IPartnerRequestQueries>();
        var firstId = long.Parse(first.Headers.GetValues("X-Request-Id").Single(), System.Globalization.CultureInfo.InvariantCulture);
        var replayId = long.Parse(replay.Headers.GetValues("X-Request-Id").Single(), System.Globalization.CultureInfo.InvariantCulture);
        var original = (await queries.GetAsync(firstId, Ct))!;
        var duplicate = (await queries.GetAsync(replayId, Ct))!;
        original.RequestBody.Should().Be(body);
        original.ResponseBody.Should().Be(await first.Content.ReadAsStringAsync(Ct));
        original.Request.StatusCode.Should().Be(201);
        original.Request.CompletedAt.Should().NotBeNull();
        original.Orders.Should().ContainSingle();
        original.Orders[0].Documents.Should().ContainSingle();
        duplicate.Request.Replayed.Should().BeTrue();
        duplicate.Orders[0].Documents[0].Id.Should().Be(original.Orders[0].Documents[0].Id);
        (await queries.ListAsync(externalId, null, Ct)).Items.Should().HaveCount(2);
        original.RequestBody.Should().NotContain(SalesApiFactory.PartnerAKey);
        using var data = JsonDocument.Parse(original.ResponseBody!);
        data.RootElement.GetProperty("data").GetProperty("total").GetProperty("amount").GetString().Should().Be("500.00");
        var invoice = data.RootElement.GetProperty("data").GetProperty("invoice");
        invoice.GetProperty("id").ReadId().Should().Be(original.Orders[0].Documents[0].Id);
        invoice.GetProperty("json").GetProperty("issuedAt").GetDateTimeOffset()
            .Should().BeCloseTo(original.Request.ReceivedAt, TimeSpan.FromTicks(10));

        using var staff = factory.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateStaffToken());
        using var detailResponse = await staff.GetAsync($"/api/v1/admin/partner-requests/{firstId}", Ct);
        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync(Ct));
        detailJson.RootElement.GetProperty("data").GetProperty("request").GetProperty("id").ReadId().Should().Be(firstId);
        using var download = await staff.GetAsync($"/api/v1/admin/documents/{original.Orders[0].Documents[0].Id}/pdf", Ct);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        (await download.Content.ReadAsByteArrayAsync(Ct)).Take(5).Should().Equal(Encoding.ASCII.GetBytes("%PDF-"));
        using var browserDownload = await staff.GetAsync($"/api/v1/admin/documents/{original.Orders[0].Documents[0].Id}/pdf-data", Ct);
        using var browserJson = JsonDocument.Parse(await browserDownload.Content.ReadAsStringAsync(Ct));
        Convert.FromBase64String(browserJson.RootElement.GetProperty("data").GetProperty("contentBase64").GetString()!)
            .Take(5).Should().Equal(Encoding.ASCII.GetBytes("%PDF-"));
        using var forbidden = await client.GetAsync($"/api/v1/admin/partner-requests/{firstId}", Ct);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Malformed_Input_Is_Recorded_Without_Creating_An_Order()
    {
        using var factory = SalesApiFactory.Create(fixture);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);
        using var response = await client.PostAsync("/api/v1/orders", new StringContent("{broken", Encoding.UTF8, "application/json"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var id = long.Parse(response.Headers.GetValues("X-Request-Id").Single(), System.Globalization.CultureInfo.InvariantCulture);
        using var scope = factory.Services.CreateScope();
        var receipt = (await scope.ServiceProvider.GetRequiredService<IPartnerRequestQueries>().GetAsync(id, Ct))!;
        receipt.RequestBody.Should().Be("{broken");
        receipt.ResponseBody.Should().Be(await response.Content.ReadAsStringAsync(Ct));
        receipt.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task Oversized_Input_Is_Refused_Before_Persistence()
    {
        using var factory = SalesApiFactory.Create(fixture);
        using var client = SalesApiFactory.Client(factory, SalesApiFactory.PartnerAKey);
        var correlation = $"oversize-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlation);
        using var response = await client.PostAsync("/api/v1/orders", new StringContent(new string('x', 1_048_577), Encoding.UTF8, "application/json"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<MkbjDbContext>().PartnerOrderRequests.AnyAsync(x => x.CorrelationId == correlation, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Scalar_Exposes_A_Usable_Snake_Case_Request_And_Partner_Authentication()
    {
        using var factory = SalesApiFactory.Create(fixture);
        using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", Ct));
        var paths = json.RootElement.GetProperty("paths");
        var operation = paths.EnumerateObject().Single(x => x.Name.TrimEnd('/') == "/api/v1/orders").Value.GetProperty("post");
        var request = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json");
        request.GetProperty("example").GetProperty("amount").GetString().Should().Be("500.00");
        request.GetProperty("schema").GetProperty("properties").TryGetProperty("external_order_id", out _).Should().BeTrue();
        operation.GetProperty("security")[0].TryGetProperty("PartnerApiKey", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_Failed_Response_Record_Leaves_The_Committed_Order_Discoverable_And_Retryable()
    {
        using var failing = SalesApiFactory.Create(fixture, services =>
            services.Decorate<IPartnerOrderRequestJournal, FailingCompletion>());
        await SalesTestData.EnsureSeededAsync(failing.Services, Ct);
        using var client = SalesApiFactory.Client(failing, SalesApiFactory.PartnerAKey);
        var external = $"interrupted-{Guid.NewGuid():N}";
        var body = JsonSerializer.Serialize(new { external_order_id = external, external_customer_id = external,
            amount = "500.00", currency = "USD", customer = new { email = "retry@example.test", phone = "+6591234567" } });
        using var failed = await client.PostAsync("/api/v1/orders", new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var scope = failing.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IPartnerRequestQueries>();
        var receipt = (await queries.ListAsync(external, null, Ct)).Items.Single();
        receipt.CompletedAt.Should().BeNull();
        var detail = (await queries.GetAsync(receipt.Id, Ct))!;
        detail.Orders.Should().ContainSingle();
        using var healthy = SalesApiFactory.Create(fixture);
        using var retryClient = SalesApiFactory.Client(healthy, SalesApiFactory.PartnerAKey);
        using var retry = await retryClient.PostAsync("/api/v1/orders", new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        using var result = JsonDocument.Parse(await retry.Content.ReadAsStringAsync(Ct));
        result.RootElement.GetProperty("data").GetProperty("id").ReadId().Should().Be(detail.Orders[0].Id);
    }

    private sealed class FailingCompletion(IPartnerOrderRequestJournal inner) : IPartnerOrderRequestJournal
    {
        public Task ReceiveAsync(PartnerOrderRequest request, CancellationToken ct) => inner.ReceiveAsync(request, ct);
        public Task CompleteAsync(long id, DateTimeOffset at, int statusCode, string responseBody, bool replayed, CancellationToken ct) =>
            throw new IOException("Simulated response-journal failure after order commit.");
    }
}
