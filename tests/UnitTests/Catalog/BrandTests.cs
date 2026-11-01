using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Brands.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// A brand is one of the few catalogue facts an import creates on its own, so the aggregate has
/// to tolerate the same brand arriving spelled three ways without producing three brands.
/// </summary>
public sealed class BrandTests
{
    private static readonly DateTimeOffset T0 = TestTime.Now;
    private static readonly DateTimeOffset T1 = TestTime.Now.AddHours(1);

    private static Brand Create(string displayName = "Northwind", string provenance = "admin") =>
        Brand.Create(1, displayName, provenance, T0);

    [Fact]
    public void Create_Normalizes_CanonicalName_And_Keeps_DisplayName()
    {
        var brand = Create("  Northwind Labs ");

        brand.DisplayName.Should().Be("Northwind Labs");
        brand.CanonicalName.Should().Be("northwind-labs");
        brand.IsActive.Should().BeTrue();
        brand.Provenance.Should().Be("admin");
        brand.CreatedAt.Should().Be(T0);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("import")]
    [InlineData("seed")]
    public void Create_Accepts_Each_Known_Provenance(string provenance) =>
        FluentActions.Invoking(() => Create(provenance: provenance)).Should().NotThrow();

    [Theory]
    [InlineData("Admin")]
    [InlineData("crawler")]
    [InlineData("")]
    public void Create_Refuses_An_Unknown_Provenance(string provenance) =>
        FluentActions.Invoking(() => Create(provenance: provenance))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("BRAND_PROVENANCE_INVALID",
                "provenance decides whether a later import may overwrite the brand, so it is a "
                + "closed set rather than a free-text note");

    [Fact]
    public void Create_Refuses_A_Name_That_Canonicalises_To_Nothing() =>
        FluentActions.Invoking(() => Create(" -- "))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("CANONICAL_KEY_EMPTY");

    [Fact]
    public void AddAlias_Stores_Canonical_Form_Once()
    {
        var brand = Create();

        brand.AddAlias(2, "North Wind", T1);
        brand.AddAlias(3, "north-wind", T1);
        brand.AddAlias(4, "NORTHWIND", T1);

        brand.Aliases.Select(alias => alias.Alias).Should().Equal("north-wind");
    }

    [Fact]
    public void AddAlias_Ignores_An_Alias_Equal_To_The_Canonical_Name()
    {
        var brand = Create();

        brand.AddAlias(2, "Northwind", T1);

        brand.Aliases.Should().BeEmpty(
            "it would match nothing the canonical name does not already match");
    }

    [Fact]
    public void AddAlias_Skips_An_Alias_With_No_Letters_Or_Digits()
    {
        var brand = Create();

        brand.AddAlias(2, " -- ", T1);

        brand.Aliases.Should().BeEmpty(
            "an import's brand column holds whatever the supplier put there");
    }

    [Fact]
    public void AddAlias_Stamps_The_Update_Time_Only_When_It_Adds_Something()
    {
        var brand = Create();
        brand.AddAlias(2, "North Wind", T1);
        brand.UpdatedAt.Should().Be(T1);

        brand.AddAlias(3, "north-wind", T1.AddHours(1));

        brand.UpdatedAt.Should().Be(T1, "the second call changed nothing");
    }

    [Fact]
    public void RemoveAlias_Takes_Any_Spelling_Of_It()
    {
        var brand = Create();
        brand.AddAlias(2, "North Wind", T1);

        brand.RemoveAlias("NORTH   WIND", T1);

        brand.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void RemoveAlias_Is_Silent_About_One_That_Is_Not_There()
    {
        var brand = Create();

        FluentActions.Invoking(() => brand.RemoveAlias("Nothing", T1)).Should().NotThrow();
        brand.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Rename_Rederives_The_Canonical_Name()
    {
        var brand = Create();

        brand.Rename("Northwind Trading", T1);

        brand.DisplayName.Should().Be("Northwind Trading");
        brand.CanonicalName.Should().Be("northwind-trading");
        brand.UpdatedAt.Should().Be(T1);
    }

    [Fact]
    public void Renaming_Onto_An_Existing_Alias_Drops_That_Alias()
    {
        var brand = Create();
        brand.AddAlias(2, "Northwind Trading", T1);

        brand.Rename("Northwind Trading", T1);

        brand.Aliases.Should().BeEmpty(
            "the alias now equals the canonical name, and a row that duplicates it would be "
            + "rejected by the globally unique alias index the first time anything saves");
    }

    [Fact]
    public void SetActive_Flips_The_Flag_And_Stamps_The_Time()
    {
        var brand = Create();

        brand.SetActive(false, T1);

        brand.IsActive.Should().BeFalse();
        brand.UpdatedAt.Should().Be(T1);
    }

    [Fact]
    public void SetActive_To_The_Current_Value_Changes_Nothing()
    {
        var brand = Create();

        brand.SetActive(true, T1);

        brand.UpdatedAt.Should().BeNull();
    }
}
