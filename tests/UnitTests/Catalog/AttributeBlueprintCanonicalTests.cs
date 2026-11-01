using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// An attribute blueprint's canonical key is what an import matches a supplier's attribute column
/// against, and its scope is what stops "Size" meaning shoe size in one branch of the catalogue
/// and screen size in another.
/// </summary>
public sealed class AttributeBlueprintCanonicalTests
{
    private static readonly DateTimeOffset T0 = TestTime.Now;
    private static readonly DateTimeOffset T1 = TestTime.Now.AddHours(1);

    private static AttributeBlueprint Create(
        string name = "Screen Size",
        AttributeBlueprintDataType dataType = AttributeBlueprintDataType.Number) =>
        AttributeBlueprint.Create(1, name, dataType, T0);

    [Fact]
    public void Create_Is_Global_With_CanonicalKey()
    {
        var blueprint = Create();

        blueprint.CanonicalKey.Should().Be("screen-size");
        blueprint.Scope.Should().Be(AttributeScope.Global);
        blueprint.ScopeCategoryId.Should().BeNull();
        blueprint.UnitId.Should().BeNull();
        blueprint.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void Rename_Rederives_The_Key()
    {
        var blueprint = Create();

        blueprint.Rename("Display Size", T1);

        blueprint.CanonicalKey.Should().Be("display-size",
            "the key follows the name, so a rename must not leave the import matching the old one");
    }

    [Fact]
    public void UpdateDetails_Rederives_The_Key()
    {
        var blueprint = Create();

        blueprint.UpdateDetails("Display Size", AttributeBlueprintDataType.Number, true, [], T1);

        blueprint.CanonicalKey.Should().Be("display-size");
    }

    [Fact]
    public void Category_Scope_Requires_Category_Id() =>
        FluentActions.Invoking(() => Create().SetScope(AttributeScope.Category, null, T1))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("ATTRIBUTE_SCOPE_INVALID");

    [Fact]
    public void Category_Scope_Requires_A_Positive_Category_Id() =>
        FluentActions.Invoking(() => Create().SetScope(AttributeScope.Category, 0, T1))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("ATTRIBUTE_SCOPE_INVALID");

    [Fact]
    public void Global_Scope_Refuses_A_Category_Id() =>
        FluentActions.Invoking(() => Create().SetScope(AttributeScope.Global, 42, T1))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("ATTRIBUTE_SCOPE_INVALID",
                "a global attribute pinned to one category is a contradiction the unique index "
                + "would then read as a category-scoped row");

    [Fact]
    public void An_Undefined_Scope_Is_Refused() =>
        FluentActions.Invoking(() => Create().SetScope((AttributeScope)99, 42, T1))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("ATTRIBUTE_SCOPE_INVALID");

    [Fact]
    public void Scoping_To_A_Category_Records_Both_And_Stamps_The_Time()
    {
        var blueprint = Create();

        blueprint.SetScope(AttributeScope.Category, 42, T1);

        blueprint.Scope.Should().Be(AttributeScope.Category);
        blueprint.ScopeCategoryId.Should().Be(42);
        blueprint.UpdatedAt.Should().Be(T1);
    }

    [Fact]
    public void Returning_To_Global_Clears_The_Category()
    {
        var blueprint = Create();
        blueprint.SetScope(AttributeScope.Category, 42, T1);

        blueprint.SetScope(AttributeScope.Global, null, T1);

        blueprint.Scope.Should().Be(AttributeScope.Global);
        blueprint.ScopeCategoryId.Should().BeNull();
    }

    [Fact]
    public void SetAliases_Canonicalizes_Dedupes_And_Drops_Own_Key()
    {
        var blueprint = Create();

        blueprint.SetAliases(["Display Size", "display-size", "SCREEN SIZE"], T1);

        blueprint.Aliases.Should().BeEquivalentTo(["display-size"],
            "an alias equal to the attribute's own key matches nothing the key does not already match");
    }

    [Fact]
    public void SetAliases_Replaces_Rather_Than_Appends()
    {
        var blueprint = Create();
        blueprint.SetAliases(["Display Size"], T1);

        blueprint.SetAliases(["Panel Size"], T1);

        blueprint.Aliases.Should().BeEquivalentTo(["panel-size"]);
    }

    [Fact]
    public void An_Alias_With_No_Letters_Or_Digits_Is_Dropped_Not_Fatal()
    {
        var blueprint = Create();

        blueprint.SetAliases(["Display Size", " -- ", ""], T1);

        blueprint.Aliases.Should().BeEquivalentTo(["display-size"],
            "an import's header row holds whatever the supplier put there; one unusable entry "
            + "must not reject the rest");
    }

    [Fact]
    public void SetAliases_Refuses_More_Than_Twenty() =>
        FluentActions.Invoking(() => Create()
                .SetAliases(Enumerable.Range(0, 21).Select(i => $"alias-{i}").ToList(), T1))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("ATTRIBUTE_ALIASES_TOO_MANY");

    [Fact]
    public void Twenty_Aliases_Are_Accepted() =>
        FluentActions.Invoking(() => Create()
                .SetAliases(Enumerable.Range(0, 20).Select(i => $"alias-{i}").ToList(), T1))
            .Should().NotThrow();

    [Fact]
    public void SetUnit_Records_The_Unit()
    {
        var blueprint = Create();

        blueprint.SetUnit(7, T1);

        blueprint.UnitId.Should().Be(7);
        blueprint.UpdatedAt.Should().Be(T1);
    }

    [Fact]
    public void SetUnit_Clears_With_Null()
    {
        var blueprint = Create();
        blueprint.SetUnit(7, T1);

        blueprint.SetUnit(null, T1);

        blueprint.UnitId.Should().BeNull();
    }

    [Fact]
    public void SetUnit_Refuses_A_Non_Positive_Id() =>
        FluentActions.Invoking(() => Create().SetUnit(0, T1))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("ATTRIBUTE_UNIT_INVALID");

    [Fact]
    public void A_Name_With_No_Letters_Or_Digits_Is_Refused() =>
        FluentActions.Invoking(() => Create(" -- ", AttributeBlueprintDataType.Text))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("CANONICAL_KEY_EMPTY",
                "a name that canonicalises to nothing can never be matched by an import");

    [Fact]
    public void Option_Is_A_Data_Type_Whose_Values_Are_Labels()
    {
        var blueprint = Create("Colour", AttributeBlueprintDataType.Option);

        blueprint.AddOption(10, "  Midnight Blue  ", null, T1);

        blueprint.Options.Should().ContainSingle()
            .Which.Value.Should().Be("Midnight Blue",
                "an option value is a label, trimmed but otherwise kept as the admin typed it");
    }

    [Fact]
    public void An_Option_Value_Cannot_Carry_A_Unit() =>
        FluentActions.Invoking(() => Create("Colour", AttributeBlueprintDataType.Option)
                .AddOption(10, "Blue", 7, T1))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_UNIT_NOT_ALLOWED");

    [Fact]
    public void Option_Is_Value_Five_So_The_Existing_Rows_Keep_Their_Meaning() =>
        ((int)AttributeBlueprintDataType.Option).Should().Be(5);

    [Fact]
    public void A_Deleted_Blueprint_Refuses_Scope_Alias_And_Unit_Changes()
    {
        var blueprint = Create();
        blueprint.Delete(T1);

        FluentActions.Invoking(() => blueprint.SetScope(AttributeScope.Category, 42, T1))
            .Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_DELETED");
        FluentActions.Invoking(() => blueprint.SetAliases(["x"], T1))
            .Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_DELETED");
        FluentActions.Invoking(() => blueprint.SetUnit(7, T1))
            .Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_DELETED");
    }
}
