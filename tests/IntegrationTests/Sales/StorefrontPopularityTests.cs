using AwesomeAssertions;
using EwaysVNext.IntegrationTests.Support;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// Best sellers, now that there are sales to count.
/// </summary>
/// <remarks>
/// Phase 3 shipped <c>popularity_30d</c> as a literal zero and <c>mv_home</c> emitted no best
/// sellers at all — an empty section is honest where a fabricated one is not. This is the phase 4
/// obligation ADR-0020 recorded, and these tests are what say it was met.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class StorefrontPopularityTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Best_Sellers_Rank_By_Units_Sold_In_The_Last_Thirty_Days()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);

        // P1 outsells P2 three to one, across two orders, because popularity is units summed
        // over a window and not a count of orders.
        await SellAsync(factory, shop.P1, quantity: 4);
        await SellAsync(factory, shop.P1, quantity: 2);
        await SellAsync(factory, shop.P2, quantity: 2);

        await RefreshAsync(factory);

        (await PopularityAsync(shop.P1)).Should().Be(6);
        (await PopularityAsync(shop.P2)).Should().Be(2);

        var bestSellers = await BestSellersAsync();
        bestSellers.Should().NotBeEmpty("the section was empty in phase 3 and must not be now");
        bestSellers.Should().ContainInOrder(shop.P1, shop.P2);
    }

    [Fact]
    public async Task A_Cancelled_Order_Is_Not_Evidence_Of_Demand()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);

        await SellAsync(factory, shop.P1, quantity: 5);
        var cancelled = await SellAsync(factory, shop.P1, quantity: 3);

        await ExecuteAsync(
            "UPDATE sales.orders SET order_status = 'Cancelled' WHERE id = @id", ("id", cancelled));

        await RefreshAsync(factory);

        (await PopularityAsync(shop.P1)).Should().Be(5, "a sale that was undone is not a sale");
    }

    [Fact]
    public async Task An_Order_Older_Than_Thirty_Days_Drops_Out()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);

        await SellAsync(factory, shop.P1, quantity: 5);
        var old = await SellAsync(factory, shop.P1, quantity: 3);

        await ExecuteAsync(
            "UPDATE sales.orders SET created_at = now() - interval '31 days' WHERE id = @id",
            ("id", old));

        await RefreshAsync(factory);

        // "Best sellers" means lately. Without the window a product that sold well once would
        // sit at the top of the homepage for ever.
        (await PopularityAsync(shop.P1)).Should().Be(5);
    }

    [Fact]
    public async Task A_Product_Nobody_Bought_Is_Not_A_Best_Seller()
    {
        using var factory = fixture.CreateFactory();
        var shop = await ScenarioAsync(factory);

        await SellAsync(factory, shop.P1, quantity: 1);
        await RefreshAsync(factory);

        (await PopularityAsync(shop.P2)).Should().Be(0, "popularity is a left join, not a filter");
        (await BestSellersAsync()).Should().NotContain(shop.P2);
    }

    private static async Task<StorefrontScenario> ScenarioAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();

        return await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);
    }

    /// <summary>One real partner order for one real product, in the real tables.</summary>
    private static async Task<long> SellAsync(
        WebApplicationFactory<Program> factory, long productId, long quantity)
    {
        using var scope = factory.Services.CreateScope();

        var order = SalesOrderMother.PartnerOrder(
            SalesOrderMother.NextId(), $"pop-{SalesOrderMother.NextId()}",
            quantity: quantity, productId: productId);

        (await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
            .TryInsertPartnerOrderAsync(order, Ct)).Should().BeTrue();

        return order.Id;
    }

    private static async Task RefreshAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();

        // The call the RefreshStorefrontViews job makes, so this exercises the production path
        // rather than a REFRESH written for the test.
        await scope.ServiceProvider.GetRequiredService<IStorefrontViewRefresher>()
            .RefreshAllAsync(Ct);
    }

    private async Task<long> PopularityAsync(long productId)
    {
        var rows = await QueryAsync(
            "SELECT popularity_30d FROM catalog.mv_storefront_product "
            + "WHERE product_id = @id AND currency = 'USD'",
            ("id", productId));

        return rows.Should().ContainSingle("the product has a USD storefront row").Subject;
    }

    /// <summary>The homepage's best sellers, in the order it would show them.</summary>
    private Task<List<long>> BestSellersAsync() => QueryAsync(
        "SELECT product_id FROM catalog.mv_home "
        + "WHERE section = 'best_sellers' AND currency = 'USD' ORDER BY rank");

    private async Task<List<long>> QueryAsync(
        string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync(Ct);

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        var rows = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct)) rows.Add(reader.GetInt64(0));

        return rows;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync(Ct);

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        await command.ExecuteNonQueryAsync(Ct);
    }
}
