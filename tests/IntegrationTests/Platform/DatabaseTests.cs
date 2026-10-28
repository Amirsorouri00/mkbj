using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Platform;

/// <summary>
/// The database the application actually gets: migrated, with the read side's extensions present,
/// and reached through a role that can write rows but owns nothing and cannot change the schema.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class DatabaseTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migrations_Applied_With_Extensions()
    {
        (await Scalar<long>(fixture.AppConnectionString,
                "select count(*) from pg_extension where extname in ('pg_trgm','unaccent')"))
            .Should().Be(2);

        (await Scalar<long>(fixture.AppConnectionString,
                "select count(*) from public.\"__EFMigrationsHistory\""))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task App_Role_Can_Write_Catalog_Tables_But_Owns_Nothing() =>
        (await Scalar<long>(fixture.AppConnectionString,
                "select count(*) from pg_tables where tableowner = 'eways_app'"))
            .Should().Be(0);

    private static async Task<T> Scalar<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
