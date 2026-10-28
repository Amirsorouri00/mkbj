using System.Globalization;
using AwesomeAssertions;
using EwaysVNext.Persistence;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// Money is stored in minor units (ADR-0017 D3). The conversion must be exact in both directions
/// and must refuse anything it cannot represent, because the alternative is silent rounding — and
/// the composition engine's whole job is to hit a requested amount to the cent.
/// </summary>
public sealed class MinorUnitsTests
{
    [Theory]
    [InlineData("10.00", 1000L)]
    [InlineData("17.43", 1743L)]
    [InlineData("500.00", 50000L)]
    [InlineData("12845.99", 1284599L)]
    [InlineData("1000000.00", 100000000L)]
    [InlineData("0.01", 1L)]
    [InlineData("-5.00", -500L)]
    public void ToMinor_Is_Exact(string amount, long minor) =>
        MinorUnits.ToMinor(decimal.Parse(amount, CultureInfo.InvariantCulture)).Should().Be(minor);

    [Fact]
    public void ToMinor_Rejects_Three_Decimals() =>
        FluentActions.Invoking(() => MinorUnits.ToMinor(1.005m)).Should().Throw<ArgumentException>();

    [Fact]
    public void ToMinor_Rejects_Overflow() =>
        FluentActions.Invoking(() => MinorUnits.ToMinor(decimal.MaxValue)).Should().Throw<OverflowException>();

    [Fact]
    public void FromMinor_Round_Trips() =>
        MinorUnits.FromMinor(1284599L).Should().Be(12845.99m);
}
