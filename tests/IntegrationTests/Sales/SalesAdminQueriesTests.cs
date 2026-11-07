using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Domain.Telemetry;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.QueryApplication.Admin;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// The backoffice read side, including the dashboard aggregates phase 6 will compose.
/// </summary>
/// <remarks>
/// The dashboard is the part that earns a real database: its composition statistics are raw
/// Postgres — <c>percentile_cont</c> has no LINQ translation — and computing them in memory
/// would mean pulling every attempt row out of the database to find one number.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class SalesAdminQueriesTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Orders_List_Filters_By_Channel_Partner_And_Number()
    {
        using var factory = fixture.CreateFactory();
        var partnerId = SalesOrderMother.NextId();
        var order = await InsertAsync(factory, partnerId);

        var queries = Queries(factory, out var scope);
        using (scope)
        {
            var mine = (await queries.ListOrdersAsync(
                new AdminOrderFilter(PartnerId: partnerId), Ct)).Value!;

            var item = mine.Items.Should().ContainSingle().Subject;
            item.Id.Should().Be(order);
            item.Channel.Should().Be("PartnerApi");
            item.RefundedMinor.Should().Be(0);

            // Every filter narrows; a wrong one returns nothing rather than everything.
            (await queries.ListOrdersAsync(
                new AdminOrderFilter(PartnerId: partnerId, Channel: AdminChannelFilter.Website), Ct))
                .Value!.Items.Should().BeEmpty();

            (await queries.ListOrdersAsync(
                new AdminOrderFilter(PartnerId: partnerId, OrderStatus: "Cancelled"), Ct))
                .Value!.Items.Should().BeEmpty();

            (await queries.ListOrdersAsync(new AdminOrderFilter(PageSize: 0), Ct))
                .Error!.Code.Should().Be("VALIDATION_FAILED");

            (await queries.ListOrdersAsync(new AdminOrderFilter(PageSize: 201), Ct))
                .Error!.Code.Should().Be("VALIDATION_FAILED");
        }
    }

    [Fact]
    public async Task Order_Detail_Carries_The_Composition_Trace_For_A_Partner_Order()
    {
        using var factory = fixture.CreateFactory();
        var orderId = await InsertAsync(factory, SalesOrderMother.NextId());

        var queries = Queries(factory, out var scope);
        using (scope)
        {
            var detail = (await queries.GetOrderAsync(orderId, Ct)).Value!;

            detail.Summary.Id.Should().Be(orderId);
            detail.Lines.Should().NotBeEmpty();
            detail.Payments.Should().ContainSingle();
            detail.Documents.Should().ContainSingle()
                .Which.Type.Should().Be("Commercial");

            // The reproduction recipe: the engine is pure, so seed plus version replays the
            // exact basket years later.
            detail.CompositionTrace.Should().NotBeNull();
            detail.CompositionTrace!.Seed.Should().Be(7);
            detail.CompositionTrace.StrategyVersion.Should().Be("v1");

            (await queries.GetOrderAsync(-1, Ct)).Error!.Code.Should().Be("NOT_FOUND");
        }
    }

    [Fact]
    public async Task The_Dashboard_Counts_Sales_And_Compositions_Over_Its_Windows()
    {
        using var factory = fixture.CreateFactory();

        var partnerId = SalesOrderMother.NextId();
        var recent = await InsertAsync(factory, partnerId);
        var old = await InsertAsync(factory, partnerId);
        var cancelled = await InsertAsync(factory, partnerId);

        await AgeAsync(old, "3 days");
        await CancelAsync(cancelled);

        // Ten attempts inside 24 hours, two of them failures, with known durations.
        var now = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 8; i++) await RecordAsync(factory, now, true, null, i * 100);
        await RecordAsync(factory, now, false, "COMPOSITION_TIMEOUT", 900);
        await RecordAsync(factory, now, false, "EXACT_COMPOSITION_UNAVAILABLE", 1_000);
        await RecordAsync(factory, now.AddDays(-3), true, null, 50);

        var queries = Queries(factory, out var scope);
        using (scope)
        {
            var dashboard = await queries.GetDashboardAsync(now, Ct);

            var day = dashboard.OrderTotals
                .Single(t => t.Window == "24h" && t.Channel == "PartnerApi" && t.Currency == "USD");

            day.OrderCount.Should().BeGreaterThanOrEqualTo(1);

            var week = dashboard.OrderTotals
                .Single(t => t.Window == "7d" && t.Channel == "PartnerApi" && t.Currency == "USD");

            week.OrderCount.Should().BeGreaterThan(day.OrderCount,
                "the three-day-old order is inside the week and outside the day");

            dashboard.OrderTotals.Should().NotContain(t => t.OrderCount == 0);

            dashboard.Composition24h.Attempts.Should().BeGreaterThanOrEqualTo(10);
            dashboard.Composition24h.Succeeded.Should().BeGreaterThanOrEqualTo(8);
            dashboard.Composition24h.SuccessRate.Should().BeInRange(0m, 1m);
            dashboard.Composition24h.P95DurationMs.Should().NotBeNull();

            dashboard.Composition7d.Attempts.Should()
                .BeGreaterThan(dashboard.Composition24h.Attempts,
                    "the three-day-old attempt is in the week only");

            dashboard.RecentFailures.Select(f => f.ErrorCode)
                .Should().Contain(["COMPOSITION_TIMEOUT", "EXACT_COMPOSITION_UNAVAILABLE"]);

            dashboard.RecentFailures.Should().HaveCountLessThanOrEqualTo(20);

            _ = recent;
        }
    }

    private static ISalesAdminQueries Queries(
        WebApplicationFactory<Program> factory, out IServiceScope scope)
    {
        scope = factory.Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<ISalesAdminQueries>();
    }

    private static async Task<long> InsertAsync(
        WebApplicationFactory<Program> factory, long partnerId)
    {
        using var scope = factory.Services.CreateScope();

        var order = SalesOrderMother.PartnerOrder(partnerId, $"AD-{SalesOrderMother.NextId()}");

        (await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
            .TryInsertPartnerOrderAsync(order, Ct)).Should().BeTrue();

        return order.Id;
    }

    private static async Task RecordAsync(
        WebApplicationFactory<Program> factory, DateTimeOffset at, bool succeeded,
        string? errorCode, long durationMs)
    {
        using var scope = factory.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ICompositionAttemptRecorder>()
            .RecordAsync(
                new CompositionAttemptRecord(
                    at, PartnerId: 7, Currency: "USD", TargetMinor: 100_000, succeeded,
                    errorCode, durationMs, Lines: succeeded ? 3 : 0, Iterations: 2,
                    CandidatePoolSize: 50, Seed: SalesOrderMother.NextId(),
                    StrategyVersion: "v1", Recompositions: 0, OrderId: null),
                Ct);
    }

    private Task AgeAsync(long orderId, string interval) => ExecuteAsync(
        $"UPDATE sales.orders SET created_at = now() - interval '{interval}' WHERE id = @id",
        orderId);

    private Task CancelAsync(long orderId) => ExecuteAsync(
        "UPDATE sales.orders SET order_status = 'Cancelled' WHERE id = @id", orderId);

    private async Task ExecuteAsync(string sql, long orderId)
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync(Ct);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", orderId);

        await command.ExecuteNonQueryAsync(Ct);
    }
}
