using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Money;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Money;

/// <summary>
/// The one amount format Sales accepts on the wire.
/// </summary>
/// <remarks>
/// A partner sends the amount it wants a basket to total, and the whole composition is built to
/// hit it exactly. "500" could mean five hundred or five, and "500,00" is five hundred in half of
/// Europe and five in the other half — so nothing but two decimal digits is accepted, and an
/// ambiguous amount is refused rather than guessed.
/// </remarks>
public sealed class AmountTextTests
{
    [Theory]
    [InlineData("500")]
    [InlineData("500.1")]
    [InlineData("500.123")]
    [InlineData("1e3")]
    [InlineData("-5.00")]
    [InlineData(" 500.00")]
    [InlineData("500.00 ")]
    [InlineData("500,00")]
    [InlineData("+500.00")]
    [InlineData(".50")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("99999999999999999999.00")]
    public void Rejects_Anything_But_Two_Decimal_Digits(string? text) =>
        AmountText.TryParseMinor(text, out _).Should().BeFalse();

    [Theory]
    [InlineData("10.00", 1000L)]
    [InlineData("17.43", 1743L)]
    [InlineData("500.00", 50000L)]
    [InlineData("12845.99", 1284599L)]
    [InlineData("1000000.00", 100000000L)]
    [InlineData("0.01", 1L)]
    [InlineData("0.00", 0L)]
    public void Parses_Two_Decimal_Strings(string text, long expected)
    {
        AmountText.TryParseMinor(text, out var minor).Should().BeTrue();

        minor.Should().Be(expected);
        AmountText.Format(minor).Should().Be(text, "the round trip is what a client sees back");
    }

    [Fact]
    public void An_Amount_Too_Large_For_Minor_Units_Is_Refused_Rather_Than_Wrapped()
    {
        // long.MaxValue is 9_223_372_036_854_775_807 minor units; a hundredth more overflows.
        AmountText.TryParseMinor("92233720368547758.08", out _).Should().BeFalse(
            "a wrapped amount would compose a basket against a negative target");
    }

    [Fact]
    public void Format_Is_Invariant() =>
        // A thread under a comma-decimal culture must not emit "500,00" into a JSON body.
        AmountText.Format(50000).Should().Be("500.00");
}
