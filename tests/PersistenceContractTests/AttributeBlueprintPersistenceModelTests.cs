using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EwaysVNext.PersistenceContractTests;

/// <summary>
/// The AttributeBlueprint mapping contract. Names are asserted in the snake_case the Postgres
/// convention produces (ADR-0017); filters are asserted in Postgres syntax, because a filter is
/// raw SQL the provider does not translate.
/// </summary>
public sealed class AttributeBlueprintPersistenceModelTests
{
    private static IEntityType Blueprint()
    {
        using var db = Providers.Npgsql<EwaysVNextDbContext>();
        return db.Model.FindEntityType(typeof(AttributeBlueprint))!;
    }

    [Fact]
    public void Model_Uses_Catalog_Schema_And_Snake_Case_Table_Names()
    {
        var blueprint = Blueprint();

        blueprint.GetSchema().Should().Be("catalog");
        blueprint.GetTableName().Should().Be("attribute_blueprints");
    }

    [Fact]
    public void CanonicalKey_Is_A_Required_Bounded_String()
    {
        var key = Blueprint().FindProperty(nameof(AttributeBlueprint.CanonicalKey))!;

        key.IsNullable.Should().BeFalse();
        key.GetMaxLength().Should().Be(120);
    }

    [Fact]
    public void Aliases_Map_To_A_Text_Array_Read_Through_The_Backing_Field()
    {
        var aliases = Blueprint().FindProperty("Aliases")!;

        aliases.GetColumnName().Should().Be("aliases");
        aliases.IsNullable.Should().BeFalse();
        aliases.GetFieldName().Should().Be("_aliases",
            "the list is private; going through the property would bypass SetAliases");
    }

    [Fact]
    public void Scope_Is_Stored_As_An_Integer()
    {
        var scope = Blueprint().FindProperty(nameof(AttributeBlueprint.Scope))!;

        scope.GetProviderClrType().Should().Be<int>(
            "an enum stored by name would silently change meaning the day a member is renamed");
    }

    [Fact]
    public void Scope_Category_And_Unit_Ids_Are_Nullable_Bigints()
    {
        var blueprint = Blueprint();

        foreach (var name in new[]
                 {
                     nameof(AttributeBlueprint.ScopeCategoryId),
                     nameof(AttributeBlueprint.UnitId)
                 })
        {
            var property = blueprint.FindProperty(name)!;
            property.IsNullable.Should().BeTrue();
            property.GetColumnType().Should().Be("bigint");
        }
    }

    [Fact]
    public void The_Scope_Key_Is_Unique_Per_Scope_Among_Live_Rows()
    {
        // NULLS NOT DISTINCT is not visible here: Npgsql emits it during SQL generation, not as
        // model metadata. CatalogConstraintTests proves it against a real database instead.
        var index = Blueprint().GetIndexes()
            .Single(index => index.GetDatabaseName() == "ux_attribute_blueprints_scope_key");

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Be("deleted_at IS NULL");
        index.Properties.Select(property => property.Name)
            .Should().Equal(nameof(AttributeBlueprint.ScopeCategoryId),
                nameof(AttributeBlueprint.CanonicalKey));
    }

    [Fact]
    public void Aliases_Are_Indexed_For_Containment()
    {
        var index = Blueprint().GetIndexes()
            .Single(index => index.GetDatabaseName() == "ix_attribute_blueprints_aliases");

        index.GetMethod().Should().Be("gin",
            "the lookup asks which attribute answers to a column heading, which a btree cannot serve");
    }
}
