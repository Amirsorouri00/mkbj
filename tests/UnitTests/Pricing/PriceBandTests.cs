using AwesomeAssertions;
using EwaysVNext.Modules.Pricing.Application.Bands;
using Xunit;

namespace EwaysVNext.UnitTests.Pricing;

/// <summary>
/// The band a composed line's unit price has to fall inside. Every case here is a cent-level
/// boundary, because that is the only kind of error this can have.
/// </summary>
public sealed class PriceBandTests
{
    [Theory]
    [InlineData(100L, 95L, 105L)]
    [InlineData(1L, 1L, 1L)]                 // ceil(0.95) = 1, floor(1.05) = 1
    [InlineData(1999L, 1900L, 2098L)]        // ceil(1899.05), floor(2098.95)
    [InlineData(50000L, 47500L, 52500L)]
    [InlineData(19L, 19L, 19L)]              // ceil(18.05) = 19, floor(19.95) = 19
    [InlineData(100000000L, 95000000L, 105000000L)]
    public void For_Uses_Integer_Ceil_And_Floor(long reference, long lo, long hi) =>
        PriceBand.For(reference).Should().Be((lo, hi));

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void For_Rejects_Non_Positive(long reference) =>
        FluentActions.Invoking(() => PriceBand.For(reference))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void For_Overflow_Throws() =>
        FluentActions.Invoking(() => PriceBand.For(long.MaxValue / 100))
            .Should().Throw<OverflowException>(
                "a wrapped band would silently accept any price at all");

    [Theory]
    [InlineData(1L)]
    [InlineData(19L)]
    [InlineData(100L)]
    [InlineData(1999L)]
    [InlineData(123_456_789L)]
    public void The_Band_Is_Never_Wider_Than_Five_Percent(long reference)
    {
        var (lo, hi) = PriceBand.For(reference);

        // Checked with the arithmetic the other way round: lo rounds up and hi rounds down, so
        // neither bound may fall outside the exact ±5% window.
        (lo * 100).Should().BeGreaterThanOrEqualTo(reference * 95);
        (hi * 100).Should().BeLessThanOrEqualTo(reference * 105);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(19L)]
    [InlineData(100L)]
    [InlineData(1999L)]
    public void The_Reference_Price_Is_Always_Inside_Its_Own_Band(long reference)
    {
        var (lo, hi) = PriceBand.For(reference);

        reference.Should().BeGreaterThanOrEqualTo(lo).And.BeLessThanOrEqualTo(hi);
    }
}
