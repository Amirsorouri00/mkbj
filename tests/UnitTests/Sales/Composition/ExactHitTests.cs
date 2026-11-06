using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Composition;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Composition;

/// <summary>
/// Landing a basket on the requested amount to the cent.
/// </summary>
/// <remarks>
/// This is the half of composition that makes "exactly" true. The quantities are already fixed by
/// the time it runs; the only freedom left is where inside its ±5% band each unit price sits. A
/// result that is a cent off is a failed order, not a rounding detail.
/// </remarks>
public sealed class ExactHitTests
{
    private static IterationBudget Budget(int reads = 10_000) => new(reads);

    [Fact]
    public void Starts_At_Reference_When_Target_Equals_Reference_Sum() =>
        // Nothing to move, so nothing moves. A partner who asks for exactly the list total gets
        // list prices, not prices nudged around for no reason.
        ExactHit.TryAssign(
                (2 * 1234) + 999,
                [new(0, 2, 1234, 1173, 1295), new(1, 1, 999, 950, 1048)],
                Budget(100))
            .Should().Equal(1234, 999);

    [Fact]
    public void Q1_Line_Absorbs_Any_Residual()
    {
        DraftLine[] lines = [new(0, 7, 1999, 1900, 2098), new(1, 1, 50000, 47500, 52500)];
        var target = (7 * 1999) + 50000 + 3;

        var prices = ExactHit.TryAssign(target, lines, Budget())!;

        ((7 * prices[0]) + prices[1]).Should().Be(target);
        prices[0].Should().BeInRange(1900, 2098);
        prices[1].Should().BeInRange(47500, 52500);
    }

    [Fact]
    public void Target_Outside_Sum_Of_Bands_Is_Null() =>
        // A cent above what three units at the top of the band can reach. Null is a fact about
        // these quantities, not about the amount — the engine's answer is different quantities.
        ExactHit.TryAssign((3 * 1050) + 1, [new(0, 3, 1000, 950, 1050)], Budget(100))
            .Should().BeNull();

    [Fact]
    public void Gcd_That_Does_Not_Divide_Residual_Is_Null() =>
        // Quantities 2 and 4 move the total in steps of 2, so an odd residual is unreachable
        // however the prices are arranged. Recognised by the gcd rather than discovered by
        // searching.
        ExactHit.TryAssign(
                (2 * 1000) + (4 * 1000) + 1,
                [new(0, 2, 1000, 950, 1050), new(1, 4, 1000, 950, 1050)],
                Budget())
            .Should().BeNull();

    [Fact]
    public void Residual_Search_Finds_Multi_Line_Moves()
    {
        // Quantities 3 and 5, no quantity-1 line, residual 1. No single line can move the total
        // by one — but −3 on the first and +2 on the second is +1 overall.
        DraftLine[] lines = [new(0, 3, 1000, 950, 1050), new(1, 5, 1000, 950, 1050)];

        var prices = ExactHit.TryAssign(8001, lines, Budget())!;

        ((3 * prices[0]) + (5 * prices[1])).Should().Be(8001);
        prices[0].Should().BeInRange(950, 1050);
        prices[1].Should().BeInRange(950, 1050);
    }

    [Fact]
    public void Every_Price_Stays_Inside_Its_Own_Band()
    {
        // A deliberately awkward mix: wide and narrow bands, big and small quantities, and a
        // target near the top of the reachable range.
        DraftLine[] lines =
        [
            new(0, 13, 499, 475, 523),
            new(1, 1, 125_000, 118_750, 131_250),
            new(2, 4, 8_999, 8_550, 9_448),
            new(3, 97, 50, 48, 52),
        ];

        var target = (13 * 520) + 130_000 + (4 * 9_400) + (97 * 51);

        var prices = ExactHit.TryAssign(target, lines, Budget())!;

        for (var i = 0; i < lines.Length; i++)
        {
            prices[i].Should().BeInRange(lines[i].Lo, lines[i].Hi,
                "a price outside its band is a price the seller never agreed to");
        }

        lines.Select((line, i) => line.Quantity * prices[i]).Sum().Should().Be(target);
    }

    /// <summary>
    /// The proportional spread can overshoot a narrow band, and must be clamped when it does.
    /// </summary>
    /// <remarks>
    /// Stage (a) divides the whole difference by each line's share of the total room, so a line
    /// with a tight band gets handed more than it can take: here it is asked to move +19 when it
    /// has +10 left. Unclamped it would sit at 1020 against a ceiling of 1010 — inside a basket
    /// that still totals correctly, so only the band check would ever catch it.
    /// </remarks>
    [Fact]
    public void A_Narrow_Band_Is_Not_Overshot_By_The_Proportional_Spread()
    {
        DraftLine[] lines = [new(0, 1, 1000, 950, 1010), new(1, 1, 1000, 950, 1200)];

        var prices = ExactHit.TryAssign(2100, lines, Budget())!;

        prices[0].Should().BeInRange(950, 1010);
        prices[1].Should().BeInRange(950, 1200);
        (prices[0] + prices[1]).Should().Be(2100);
    }

    [Theory]
    [InlineData(-120)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(120)]
    public void Hits_Targets_Above_And_Below_The_Reference_Sum(long offset)
    {
        DraftLine[] lines = [new(0, 3, 1000, 950, 1050), new(1, 1, 2000, 1900, 2100)];
        var target = (3 * 1000) + 2000 + offset;

        var prices = ExactHit.TryAssign(target, lines, Budget())!;

        ((3 * prices[0]) + prices[1]).Should().Be(target,
            "a partner may ask for less than list as readily as more");
    }

    [Fact]
    public void A_Line_With_No_Room_Cannot_Be_Moved()
    {
        // lo == hi == reference: a fixed-price line. The other line has to do all the work.
        DraftLine[] lines = [new(0, 1, 1000, 1000, 1000), new(1, 1, 2000, 1900, 2100)];

        var prices = ExactHit.TryAssign(3050, lines, Budget())!;

        prices[0].Should().Be(1000);
        prices[1].Should().Be(2050);
    }

    [Fact]
    public void An_Exhausted_Budget_Stops_The_Residual_Search_Rather_Than_Running_On() =>
        // Quantities 3 and 5 need the search; with no budget it gives up instead.
        ExactHit.TryAssign(
                8001,
                [new(0, 3, 1000, 950, 1050), new(1, 5, 1000, 950, 1050)],
                new ExpiredBudget())
            .Should().BeNull();

    [Fact]
    public void A_Single_Fixed_Price_Line_Hits_Only_Its_Own_Total()
    {
        DraftLine[] lines = [new(0, 4, 2500, 2500, 2500)];

        ExactHit.TryAssign(10_000, lines, Budget()).Should().Equal(2500);
        ExactHit.TryAssign(10_001, lines, Budget()).Should().BeNull();
    }
}
