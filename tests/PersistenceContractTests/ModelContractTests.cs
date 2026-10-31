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

        // Asserts the rule rather than a list of known schemas: EwaysVNextDbContext.ResolveSchema
        // already throws for a namespace it cannot place, so enumerating the names here only
        // bought an edit every time a module was added — and a stale list fails a correct change.
        var schemas = Providers.Model(context).GetEntityTypes()
            .Select(entity => new { Entity = entity.ClrType.Name, Schema = entity.GetSchema() })
            .ToList();

        schemas.Should().NotBeEmpty();

        schemas.Where(entry =>
                string.IsNullOrWhiteSpace(entry.Schema)
                || entry.Schema == "public"
                || !entry.Schema!.All(character => !char.IsLetter(character) || char.IsLower(character)))
            .Should().BeEmpty("a table in the default schema is one two modules can collide in");
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
            property.IsConcurrencyToken.Should().BeFalse(
                "InMemory has no xmin, so a concurrency check against it would compare a value "
                + "nothing ever assigns");

            // OnAdd, not Never — and this is the contract, not an accident. Against Npgsql the
            // column is xmin, which the database supplies, so no HasData row carries a value for
            // it. Marked Never here it becomes a required property with no value and EF rejects
            // every seeded row in the model, which took out 54 tests unrelated to the seed.
            property.ValueGenerated.Should().Be(ValueGenerated.OnAdd);
        }
    }

    private static List<string> NonSnakeCaseNames()
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
