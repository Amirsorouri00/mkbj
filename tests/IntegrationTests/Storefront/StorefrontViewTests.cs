using AwesomeAssertions;
using EwaysVNext.Database.Catalog.Views;
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
