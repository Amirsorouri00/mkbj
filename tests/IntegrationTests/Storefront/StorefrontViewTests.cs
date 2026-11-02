using AwesomeAssertions;
using EwaysVNext.Database.Catalog.Views;
using EwaysVNext.IntegrationTests.Support;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Storefront;

/// <summary>
/// The storefront's materialized views against the migrated database.
/// </summary>
/// <remarks>
/// Both facts here are only true of real Postgres. A unique index is what makes
/// <c>REFRESH … CONCURRENTLY</c> legal at all, and refreshing concurrently is what keeps the shop
/// readable while the refresh runs — without it every refresh takes an <c>ACCESS EXCLUSIVE</c>
/// lock and the storefront stalls behind it.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class StorefrontViewTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Views() => [.. StorefrontViewSql.RefreshOrder];

    /// <summary>Each view with the index that states its grain.</summary>
    public static TheoryData<string, string> Grains() => new()
    {
        { "catalog.mv_storefront_variant", "ux_mv_sv_grain" },
        { "catalog.mv_storefront_product", "ux_mv_sp_grain" },
        { "catalog.mv_category_facets", "ux_mv_cf_grain" },
        { "catalog.mv_home", "ux_mv_home_grain" },
    };

    [Theory]
    [MemberData(nameof(Grains))]
    public async Task Every_View_Has_A_Unique_Grain_Index(string view, string indexName)
    {
        var (schema, name) = Split(view);

        var definitions = await QueryAsync(fixture.AppConnectionString,
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = @schema AND tablename = @name AND indexname = @index
            """,
            ("schema", schema), ("name", name), ("index", indexName));

        definitions.Should().ContainSingle(
            "a materialized view with no unique index cannot be refreshed concurrently")
            .Which.Should().StartWith("CREATE UNIQUE INDEX");
    }

    [Theory]
    [MemberData(nameof(Views))]
    public async Task Every_View_Refreshes_Concurrently_As_The_App_Role(string view)
    {
        // Through the function, because refreshing requires ownership and the application owns
        // nothing. This is the path the refresher takes in production.
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync(Ct);

        await using var command = new NpgsqlCommand(
            "SELECT catalog.refresh_storefront_view(@view, true)", connection);
        command.Parameters.AddWithValue("view", view);

        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>
    /// A name the migrator owns, and a name carrying a second statement.
    /// </summary>
    /// <remarks>
    /// Neither is a storefront view, and both have to be refused by the whitelist rather than by
    /// luck. <c>pg_catalog.pg_class</c> would be refused anyway — the migrator does not own it —
    /// so it proves nothing; <c>catalog.products</c> is a table the migrator <b>does</b> own, and
    /// the injected name is what a <c>SECURITY DEFINER</c> function that interpolates its
    /// argument actually costs.
    /// </remarks>
    [Theory]
    [InlineData("catalog.products")]
    [InlineData("catalog.mv_home; CREATE TABLE catalog.owned_by_injection (id int)")]
    public async Task The_Refresh_Function_Refuses_Anything_That_Is_Not_A_Storefront_View(string name)
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync(Ct);

        await using var command = new NpgsqlCommand(
            "SELECT catalog.refresh_storefront_view(@view, true)", connection);
        command.Parameters.AddWithValue("view", name);

        var act = async () => await command.ExecuteNonQueryAsync(Ct);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState
            .Should().Be("42501", "the function must refuse the name, not fail by accident");
    }

    /// <summary>
    /// Acceptance 11: a variant an admin deactivates stops being purchasable at once, and leaves
    /// the storefront at the next refresh.
    /// </summary>
    /// <remarks>
    /// The gap between the two is the design, not a defect (ADR-0020). A stale listing shows a
    /// variant that can no longer be bought; a stale <i>purchasing</i> path would sell it. Only
    /// the second matters, and the live port is what purchasing reads.
    /// </remarks>
    [Fact]
    public async Task Acceptance11_Deactivated_Variant_Leaves_Plp_After_Refresh()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var scenario = await StorefrontScenario.CreateAsync(services, Ct);

        (await VariantRowsAsync(scenario.P1Blue)).Should().NotBeEmpty(
            "the scenario refreshes, so both of P1's variants are on the listing");

        Succeeded(await services.GetRequiredService<ICatalogAdminService>()
            .SetVariantActiveAsync(scenario.P1, scenario.P1Blue, active: false, scenario.Staff, Ct));

        (await services.GetRequiredService<ISellableVariantPort>()
            .GetSellableAsync([scenario.P1Blue], Ct))
            .Should().BeEmpty("purchasing reads live tables and must never lag behind a takedown");

        (await VariantRowsAsync(scenario.P1Blue)).Should().NotBeEmpty(
            "the listing is allowed to lag until the next refresh");

        await services.GetRequiredService<IStorefrontViewRefresher>().RefreshAllAsync(Ct);

        (await VariantRowsAsync(scenario.P1Blue)).Should().BeEmpty();
        (await ProductVariantCountsAsync(scenario.P1)).Should().OnlyContain(count => count == 1);
    }

    [Fact]
    public async Task Plp_Views_Never_Contain_Draft_Or_Deactivated_Products()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);

        (await ProductCurrenciesAsync(scenario.P3)).Should().BeEmpty(
            "a Draft product has a price, and a price is not permission to sell it");
        (await ProductCurrenciesAsync(scenario.P4)).Should().BeEmpty(
            "an admin took it down; it was active once, which must not matter");

        (await ProductCurrenciesAsync(scenario.P1))
            .Should().BeEquivalentTo(["USD", "EUR", "SGD", "AED", "GBP"],
                "a USD-based product is shown in every enabled currency the rates can reach");
    }

    private async Task<List<string>> VariantRowsAsync(long variantId) =>
        await QueryAsync(fixture.AppConnectionString,
            "SELECT currency FROM catalog.mv_storefront_variant WHERE variant_id = @id",
            ("id", variantId));

    private async Task<List<string>> ProductCurrenciesAsync(long productId) =>
        [.. (await QueryAsync(fixture.AppConnectionString,
            "SELECT currency FROM catalog.mv_storefront_product WHERE product_id = @id",
            ("id", productId))).Select(code => code.Trim())];

    private async Task<List<int>> ProductVariantCountsAsync(long productId) =>
        [.. (await QueryAsync(fixture.AppConnectionString,
            "SELECT variant_count::text FROM catalog.mv_storefront_product WHERE product_id = @id",
            ("id", productId))).Select(int.Parse)];

    private static void Succeeded(EwaysVNext.Outcomes.Result result) =>
        result.IsSuccess.Should().BeTrue(result.Error?.Message);

    private static (string Schema, string Name) Split(string qualified)
    {
        var parts = qualified.Split('.');
        return (parts[0], parts[1]);
    }

    private static async Task<List<string>> QueryAsync(
        string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct)) rows.Add(reader.GetString(0));

        return rows;
    }
}
