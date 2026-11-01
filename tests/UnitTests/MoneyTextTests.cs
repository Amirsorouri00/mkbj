using AwesomeAssertions;
using EwaysVNext.Presentation.Endpoint.Money;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// The wire format for money. Strictness is the whole point: anything this accepts, the API has
/// decided the meaning of on the caller's behalf.
/// </summary>
public sealed class MoneyTextTests
{
    [Theory]
    [InlineData("500.00", 50000L)]
    [InlineData("0.50", 50L)]
    [InlineData("0.00", 0L)]
    [InlineData("1000000.00", 100000000L)]
    public void TryParseMinor_Accepts_Two_Decimals(string text, long expected)
    {
        MoneyText.TryParseMinor(text, out var minor).Should().BeTrue();
        minor.Should().Be(expected);
    }

    [Theory]
    [InlineData("500")]
    [InlineData("500.1")]
    [InlineData("500.123")]
    [InlineData("1e3")]
    [InlineData("-5.00")]
    [InlineData(" 500.00")]
    [InlineData("500.00 ")]
    [InlineData("1,000.00")]
    [InlineData("+5.00")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseMinor_Rejects_Everything_Else(string? text) =>
        MoneyText.TryParseMinor(text, out _).Should().BeFalse();

    [Fact]
    public void An_Amount_Too_Large_For_Minor_Units_Is_Refused_Rather_Than_Wrapped()
    {
        var huge = new string('9', 30) + ".00";

        MoneyText.TryParseMinor(huge, out var minor).Should().BeFalse(
            "a silently wrapped amount is the worst outcome money arithmetic can have");
        minor.Should().Be(0);
    }

    [Fact]
    public void Format_Pads_Cents() => MoneyText.Format(50).Should().Be("0.50");

    [Theory]
    [InlineData(0L, "0.00")]
    [InlineData(5L, "0.05")]
    [InlineData(100L, "1.00")]
    [InlineData(123456L, "1234.56")]
    public void Format_Always_Writes_Two_Decimals(long minor, string expected) =>
        MoneyText.Format(minor).Should().Be(expected);

    [Theory]
    [InlineData("0.01")]
    [InlineData("12.34")]
    [InlineData("99999999.99")]
    public void Format_Reverses_TryParseMinor(string text)
    {
        MoneyText.TryParseMinor(text, out var minor).Should().BeTrue();

        MoneyText.Format(minor).Should().Be(text,
            "the two halves are one format; a round trip that changed the text would mean a "
            + "client reading back something it never sent");
    }
}
