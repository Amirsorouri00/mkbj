using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// The guarantees that live in the database rather than in the aggregate.
/// </summary>
/// <remarks>
/// The aggregate enforces all of these already. They are tested here because the database is
/// what survives a bug in the aggregate, a hand-written UPDATE during an incident, or a future
/// code path nobody has written yet — and these tests reach the rows the way such an UPDATE
/// would, not through the model.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class SalesSchemaTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Partner_Line_Outside_Band_Is_Rejected_By_Check_Constraint()
    {
        using var factory = fixture.CreateFactory();
        var (_, lineId, reference, quantity) =
            await SalesOrderMother.InsertPartnerOrderAsync(factory.Services);

        var floor = ((95 * reference) + 99) / 100;
        var ceiling = 105 * reference / 100;

        // Both edges are legal prices.
        await SetUnitAsync(lineId, quantity, floor);
        await SetUnitAsync(lineId, quantity, ceiling);

        (await Refused(() => SetUnitAsync(lineId, quantity, floor - 1)))
            .Should().Be("ck_order_lines_partner_band", "a cent under the floor");

        (await Refused(() => SetUnitAsync(lineId, quantity, ceiling + 1)))
            .Should().Be("ck_order_lines_partner_band", "a cent over the ceiling");
    }

    [Fact]
    public async Task A_Line_That_Was_Not_Band_Checked_Is_Not_Band_Checked_Here_Either()
    {
        using var factory = fixture.CreateFactory();
        var (_, lineId, reference, quantity) =
            await SalesOrderMother.InsertPartnerOrderAsync(factory.Services);

        await ExecuteAsync(
            "UPDATE sales.order_lines SET band_enforced = false WHERE id = @id",
            ("id", lineId));

        // A website order is sold at the displayed price, whatever that is relative to the
        // reference — the band is a partner-composition rule, not a universal one.
        await SetUnitAsync(lineId, quantity, 2 * reference);
    }

    [Fact]
    public async Task Same_Partner_External_Id_Twice_Is_A_Unique_Violation()
    {
        using var factory = fixture.CreateFactory();
        var partnerId = SalesOrderMother.NextId();
        var external = $"PO-{SalesOrderMother.NextId()}";

        using var first = factory.Services.CreateScope();
        (await first.ServiceProvider.GetRequiredService<IOrderRepository>()
            .TryInsertPartnerOrderAsync(SalesOrderMother.PartnerOrder(partnerId, external), Ct))
            .Should().BeTrue();

        using var second = factory.Services.CreateScope();
        (await second.ServiceProvider.GetRequiredService<IOrderRepository>()
            .TryInsertPartnerOrderAsync(SalesOrderMother.PartnerOrder(partnerId, external), Ct))
            .Should().BeFalse("the partner already used that external id");
    }

    [Fact]
    public async Task Two_Partners_May_Share_An_External_Order_Id()
    {
        using var factory = fixture.CreateFactory();
        var external = $"PO-{SalesOrderMother.NextId()}";

        foreach (var partnerId in new[] { SalesOrderMother.NextId(), SalesOrderMother.NextId() })
        {
            using var scope = factory.Services.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
                .TryInsertPartnerOrderAsync(SalesOrderMother.PartnerOrder(partnerId, external), Ct))
                .Should().BeTrue("external ids are a partner's own numbering, not the shop's");
        }
    }

    [Fact]
    public async Task A_Rejected_Insert_Leaves_Nothing_Tracked()
    {
        using var factory = fixture.CreateFactory();
        var partnerId = SalesOrderMother.NextId();
        var external = $"PO-{SalesOrderMother.NextId()}";

        using var first = factory.Services.CreateScope();
        await first.ServiceProvider.GetRequiredService<IOrderRepository>()
            .TryInsertPartnerOrderAsync(SalesOrderMother.PartnerOrder(partnerId, external), Ct);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNext.Database.EwaysVNextDbContext>();

        (await repository.TryInsertPartnerOrderAsync(
            SalesOrderMother.PartnerOrder(partnerId, external), Ct)).Should().BeFalse();

        // Detaching the root alone would leave its owned lines, payments and documents Added,
        // and the next save would retry exactly the insert that just failed.
        db.ChangeTracker.Entries().Should().OnlyContain(
            entry => entry.State != Microsoft.EntityFrameworkCore.EntityState.Added);
    }

    [Fact]
    public async Task A_Zero_Total_And_A_Wrong_Line_Total_Are_Both_Rejected()
    {
        using var factory = fixture.CreateFactory();
        var (orderId, lineId, _, quantity) =
            await SalesOrderMother.InsertPartnerOrderAsync(factory.Services);

        (await Refused(() => ExecuteAsync(
            "UPDATE sales.orders SET total_minor = 0, subtotal_minor = 0 WHERE id = @id",
            ("id", orderId))))
            .Should().Be("ck_orders_total_positive");

        (await Refused(() => ExecuteAsync(
            "UPDATE sales.order_lines SET line_total_minor = line_total_minor + 1 WHERE id = @id",
            ("id", lineId))))
            .Should().Be("ck_order_lines_total");

        _ = quantity;
    }

    [Fact]
    public async Task Totals_That_Do_Not_Add_Up_Are_Rejected()
    {
        using var factory = fixture.CreateFactory();
        var (orderId, _, _, _) = await SalesOrderMother.InsertPartnerOrderAsync(factory.Services);

        (await Refused(() => ExecuteAsync(
            "UPDATE sales.orders SET tax_minor = 1 WHERE id = @id", ("id", orderId))))
            .Should().Be("ck_orders_totals_add_up",
                "an order whose parts do not sum to what was charged cannot be explained");
    }

    [Fact]
    public async Task A_Partner_Order_Cannot_Lose_Its_Idempotency_Key()
    {
        using var factory = fixture.CreateFactory();
        var (orderId, _, _, _) = await SalesOrderMother.InsertPartnerOrderAsync(factory.Services);

        (await Refused(() => ExecuteAsync(
            "UPDATE sales.orders SET request_fingerprint = NULL WHERE id = @id", ("id", orderId))))
            .Should().Be("ck_orders_partner_fields",
                "without it a retry would be billed as a second order");
    }

    [Fact]
    public async Task Document_Numbers_Come_From_Separate_Sequences()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var allocator = scope.ServiceProvider.GetRequiredService<IDocumentNumberAllocator>();

        var first = await allocator.NextAsync(DocumentNumberKind.Commercial, Ct);
        var second = await allocator.NextAsync(DocumentNumberKind.Commercial, Ct);

        first.Should().StartWith("INV-");
        second.Should().StartWith("INV-");
        string.CompareOrdinal(second, first).Should().BePositive("numbers only go up");

        // Separate sequences, so an auditor reading INV-000001 upwards can see that no invoice
        // is missing — which one shared counter would make impossible.
        (await allocator.NextAsync(DocumentNumberKind.ProForma, Ct)).Should().StartWith("PF-");
        (await allocator.NextAsync(DocumentNumberKind.CreditNote, Ct)).Should().StartWith("CN-");
        (await allocator.NextAsync(DocumentNumberKind.Order, Ct)).Should().StartWith("ORD-");
    }

    private Task SetUnitAsync(long lineId, long quantity, long unitPriceMinor) =>
        ExecuteAsync(
            "UPDATE sales.order_lines SET unit_price_minor = @unit, line_total_minor = @total "
            + "WHERE id = @id",
            ("unit", unitPriceMinor), ("total", quantity * unitPriceMinor), ("id", lineId));

    /// <summary>The constraint that refused, or null if nothing did.</summary>
    private static async Task<string?> Refused(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.ConstraintName;
        }
    }

    /// <summary>
    /// Raw SQL as the application role, which is how an incident-time UPDATE would arrive.
    /// </summary>
    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync(Ct);

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        await command.ExecuteNonQueryAsync(Ct);
    }
}
