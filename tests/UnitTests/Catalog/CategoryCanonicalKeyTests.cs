using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// A category's canonical key is what an import matches against, and its aliases are how the
/// names a supplier actually uses are mapped onto it.
/// </summary>
public sealed class CategoryCanonicalKeyTests
{
    private static readonly DateTimeOffset T0 = TestTime.Now;
    private static readonly DateTimeOffset T1 = TestTime.Now.AddHours(1);

    private static Category Create(string name, string slug) =>
        Category.Create(1, name, slug, null, null, 0, T0);

    [Fact]
    public void Create_Derives_CanonicalKey_From_Name()
    {
        var category = Create("Kitchen & Dining", "kitchen-dining");

        category.CanonicalKey.Should().Be("kitchen-dining");
    }

    [Fact]
    public void Update_Rederives_The_Key_From_The_New_Name()
    {
        var category = Create("Kitchen & Dining", "kitchen-dining");

        category.Update("Kitchen and Dining", "kitchen-dining", null, 0, T1);

        category.CanonicalKey.Should().Be("kitchen-and-dining",
            "the key follows the name; it is not a slug the admin controls separately");
    }

    [Fact]
    public void SetAliases_Canonicalizes_Dedupes_And_Drops_Own_Key()
    {
        var category = Create("Kettles", "kettles");

        category.SetAliases(["Electric Kettles", "electric-kettles", "KETTLES"], T1);

        category.Aliases.Should().BeEquivalentTo(["electric-kettles"],
            "'Electric Kettles' and 'electric-kettles' are one alias, and an alias equal to the "
            + "category's own key matches nothing it would not already match");
    }

    [Fact]
    public void SetAliases_Replaces_Rather_Than_Appends()
    {
        var category = Create("Kettles", "kettles");

        category.SetAliases(["Electric Kettles"], T1);
        category.SetAliases(["Tea Makers"], T1);

        category.Aliases.Should().BeEquivalentTo(["tea-makers"],
            "an admin editing the alias list sees the whole list, so a set is a replacement");
    }

    [Fact]
    public void SetAliases_Accepts_An_Empty_List()
    {
        var category = Create("Kettles", "kettles");
        category.SetAliases(["Electric Kettles"], T1);

        category.SetAliases([], T1);

        category.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void SetAliases_Refuses_More_Than_Twenty() =>
        FluentActions.Invoking(() => Create("Kettles", "kettles")
                .SetAliases(Enumerable.Range(0, 21).Select(i => $"alias-{i}").ToList(), T1))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("CATEGORY_ALIASES_TOO_MANY");

    [Fact]
    public void Twenty_Aliases_Are_Accepted() =>
        FluentActions.Invoking(() => Create("Kettles", "kettles")
                .SetAliases(Enumerable.Range(0, 20).Select(i => $"alias-{i}").ToList(), T1))
            .Should().NotThrow();

    [Fact]
    public void An_Alias_With_No_Letters_Or_Digits_Is_Dropped_Not_Fatal()
    {
        var category = Create("Kettles", "kettles");

        category.SetAliases(["Electric Kettles", " -- ", ""], T1);

        category.Aliases.Should().BeEquivalentTo(["electric-kettles"],
            "an import's alias column holds whatever the supplier put there; one unusable entry "
            + "must not reject the rest");
    }

    [Theory]
    [InlineData("Kitchen & Dining")]
    [InlineData("厨房用品")]
    [InlineData("家電・キッチン")]
    [InlineData("เครื่องครัว")]
    [InlineData("Café & Crème")]
    [InlineData("Men's Shoes")]
    public void A_Name_In_Any_Script_With_Ordinary_Punctuation_Is_Accepted(string name) =>
        FluentActions.Invoking(() => Create(name, "slug")).Should().NotThrow(
            "the seller trades internationally; the rule that refused these was an allowlist of "
            + "Latin and Arabic ranges inherited from the market this product replaced");

    [Theory]
    [InlineData("Shoes 😀")]
    [InlineData("Shoes/Boots")]
    public void Symbols_And_Slashes_Are_Still_Refused(string name) =>
        FluentActions.Invoking(() => Create(name, "slug"))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("CATEGORY_NAME_UNSUPPORTED",
                "a slash reads as hierarchy wherever a category path is shown");

    [Fact]
    public void Setting_Aliases_Stamps_The_Update_Time()
    {
        var category = Create("Kettles", "kettles");

        category.SetAliases(["Electric Kettles"], T1);

        category.UpdatedAt.Should().Be(T1);
    }
}
