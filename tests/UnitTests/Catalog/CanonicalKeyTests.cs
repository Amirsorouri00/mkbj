using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Common;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// The canonical key is how two spellings of one thing are recognised as the same thing — across
/// an import feed, a supplier's export and whatever an admin typed. It therefore has to be
/// deterministic and lossy in exactly the ways that do not change meaning: accents, case,
/// punctuation and spacing.
/// </summary>
public sealed class CanonicalKeyTests
{
    [Theory]
    [InlineData("  Café  Crème ", "cafe-creme")]
    [InlineData("USB-C / Thunderbolt 4", "usb-c-thunderbolt-4")]
    [InlineData("Kitchen & Dining", "kitchen-dining")]
    [InlineData("ÉCRAN_LCD", "ecran-lcd")]
    [InlineData("Sony", "sony")]
    [InlineData("---Already-Hyphenated---", "already-hyphenated")]
    public void From_Normalizes(string input, string expected) =>
        CanonicalKey.From(input).Should().Be(expected);

    [Fact]
    public void Non_Latin_Script_Survives_Intact() =>
        CanonicalKey.From("لوازم خانگی").Should().Be("لوازم-خانگی",
            "stripping a script the seller trades in would collapse every product in it to nothing");

    [Theory]
    [InlineData(" -/ ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("&&&")]
    public void From_Rejects_Keys_With_No_Letters_Or_Digits(string input) =>
        FluentActions.Invoking(() => CanonicalKey.From(input))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("CANONICAL_KEY_EMPTY");

    [Fact]
    public void From_Rejects_Over_120_Chars() =>
        FluentActions.Invoking(() => CanonicalKey.From(new string('a', 121)))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("CANONICAL_KEY_TOO_LONG");

    [Fact]
    public void Exactly_120_Chars_Is_Accepted() =>
        CanonicalKey.From(new string('a', CanonicalKey.MaxLength))
            .Should().HaveLength(CanonicalKey.MaxLength);

    [Fact]
    public void TryFrom_Reports_Failure_Instead_Of_Throwing()
    {
        CanonicalKey.TryFrom("  ", out var empty).Should().BeFalse();
        empty.Should().BeEmpty();

        CanonicalKey.TryFrom("Café", out var key).Should().BeTrue();
        key.Should().Be("cafe");
    }

    [Theory]
    [InlineData("Café", "CAFE")]
    [InlineData("USB-C", "usb c")]
    [InlineData(" Sony ", "SONY")]
    public void Accents_Case_And_Punctuation_Collide_On_Purpose(string first, string second) =>
        CanonicalKey.From(first).Should().Be(CanonicalKey.From(second),
            "that is what makes a re-sent import update the same row rather than add one beside it");

    [Fact]
    public void Different_Wording_Does_Not_Collide() =>
        CanonicalKey.From("Kitchen & Dining").Should().NotBe(CanonicalKey.From("Kitchen and Dining"),
            "normalisation does not guess at wording: merging two things that were never the same "
            + "is far harder to notice than a duplicate, and an alias is how '&' is matched to 'and'");

    [Fact]
    public void Normalisation_Is_Idempotent()
    {
        var once = CanonicalKey.From("Café  Crème");

        CanonicalKey.From(once).Should().Be(once,
            "a key read back from the database must normalise to itself");
    }
}
