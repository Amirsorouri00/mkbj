using System.Text.RegularExpressions;
using AuthorizationService.Implementation.Persistence;
using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EwaysVNext.PersistenceContractTests;

/// <summary>
/// The mapping contract both DbContexts must satisfy on Postgres (ADR-0017): snake_case
/// everywhere, <c>xmin</c> as the concurrency token, client-generated Snowflake keys, the
/// extensions the read side needs, and a named schema per module — never a silent default.
/// </summary>
public sealed class ModelContractTests
{
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    [Fact]
    public void Every_Name_Is_Snake_Case() => NonSnakeCaseNames().Should().BeEmpty();

    [Fact]
    public void Every_Entity_Uses_Xmin_As_RowVersion()
    {
        using var context = Providers.Npgsql<EwaysVNextDbContext>();

        var rowVersions = Providers.Model(context).GetEntityTypes()
            .Select(e => e.FindProperty("RowVersion")).OfType<IProperty>().ToList();

        rowVersions.Should().NotBeEmpty("the model must have entities carrying RowVersion");
        foreach (var property in rowVersions)
        {
            property.GetColumnName().Should().Be("xmin");
            property.GetColumnType().Should().Be("xid");
            property.IsConcurrencyToken.Should().BeTrue();
        }
    }

    [Fact]
    public void Aggregate_Root_Keys_Are_Never_Generated()
    {
        using var context = Providers.Npgsql<EwaysVNextDbContext>();

        foreach (var entityType in Providers.Model(context).GetEntityTypes()
                     .Where(e => typeof(IAggregateRoot).IsAssignableFrom(e.ClrType)))
        {
            var key = entityType.FindPrimaryKey()!.Properties.Single();
            key.ValueGenerated.Should().Be(ValueGenerated.Never,
                "{0}.{1} is a client-generated Snowflake id", entityType.ClrType.Name, key.Name);
        }
    }

    [Fact]
    public void Extensions_Are_Declared()
    {
        using var context = Providers.Npgsql<EwaysVNextDbContext>();

        Providers.Model(context).GetPostgresExtensions().Select(e => e.Name)
            .Should().Contain(["pg_trgm", "unaccent"]);
    }

    [Fact]
    public void Every_Table_Lives_In_A_Named_Schema()
    {
        using var context = Providers.Npgsql<EwaysVNextDbContext>();

        Providers.Model(context).GetEntityTypes().Select(e => e.GetSchema())
            .Should().OnlyContain(s => s == "catalog");
    }

    [Fact]
    public void Product_Price_Is_Stored_In_Minor_Units()
    {
        using var context = Providers.Npgsql<EwaysVNextDbContext>();

        var price = Providers.Model(context).GetEntityTypes()
            .Single(e => e.ClrType == typeof(EwaysVNext.Domain.ValueObjects.Money)
                         && e.FindOwnership()!.PrincipalEntityType.ClrType.Name == "Product");

        var amount = price.FindProperty(nameof(EwaysVNext.Domain.ValueObjects.Money.Amount))!;
        amount.GetColumnName().Should().Be("price_amount_minor");
        amount.GetColumnType().Should().Be("bigint");

        var currency = price.FindProperty(nameof(EwaysVNext.Domain.ValueObjects.Money.Currency))!;
        currency.GetColumnName().Should().Be("price_currency");
        // The model holds the configured literal; Postgres reports it as character(3) in its own
        // catalog, which is the same type.
        currency.GetColumnType().Should().Be("char(3)");
    }

    [Fact]
    public void InMemory_Model_Disables_RowVersion_Concurrency()
    {
        using var context = Providers.InMemory<EwaysVNextDbContext>();

        foreach (var property in Providers.Model(context).GetEntityTypes()
                     .Select(e => e.FindProperty("RowVersion")).OfType<IProperty>())
        {
            property.IsConcurrencyToken.Should().BeFalse();
            property.ValueGenerated.Should().Be(ValueGenerated.Never);
        }
    }

    private static IReadOnlyList<string> NonSnakeCaseNames()
    {
        List<string> offenders = [];

        foreach (var context in new DbContext[]
                 {
                     Providers.Npgsql<EwaysVNextDbContext>(),
                     Providers.Npgsql<AuthorizationDbContext>()
                 })
        {
            using (context)
            {
                foreach (var entityType in Providers.Model(context).GetEntityTypes())
                {
                    Check(entityType.GetTableName(), "table", offenders);

                    var storeObject = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);

                    foreach (var property in entityType.GetProperties())
                    {
                        // xmin is a Postgres system column; it is correctly lower-case but is not ours to rename.
                        var column = storeObject is { } so ? property.GetColumnName(so) : property.GetColumnName();
                        Check(column, $"column {entityType.ClrType.Name}", offenders);
                    }

                    foreach (var index in entityType.GetIndexes())
                        Check(index.GetDatabaseName(), "index", offenders);

                    foreach (var key in entityType.GetKeys())
                        Check(key.GetName(), "key", offenders);

                    foreach (var foreignKey in entityType.GetForeignKeys())
                        Check(foreignKey.GetConstraintName(), "fk", offenders);

                    foreach (var check in entityType.GetCheckConstraints())
                        Check(check.Name, "check", offenders);
                }
            }
        }

        return offenders;

        static void Check(string? name, string kind, List<string> offenders)
        {
            if (!string.IsNullOrEmpty(name) && !SnakeCase.IsMatch(name))
                offenders.Add($"{kind}: {name}");
        }
    }
}
