using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Composition;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Composition;

/// <summary>
/// The PRNG whose output is, in effect, persisted.
/// </summary>
/// <remarks>
/// A composition's seed is stored with the order so the basket can be reproduced and explained.
/// That only holds while the sequence a seed produces never changes — which is why
/// <c>System.Random</c> is not used, and why this pins the published values rather than merely
/// checking that the numbers look random. Changing these constants would make every past order
/// irreproducible, and nothing else would notice.
/// </remarks>
public sealed class SplitMix64Tests
{
    /// <summary>The published outputs for seed 42.</summary>
    private static readonly ulong[] ExpectedForSeed42 =
    [
        0xBDD732262FEB6E95UL,
        0x28EFE333B266F103UL,
        0x47526757130F9F52UL,
    ];

    [Fact]
    public void The_Sequence_For_A_Seed_Never_Changes() =>
        Take(seed: 42, count: ExpectedForSeed42.Length).Should().Equal(ExpectedForSeed42,
            "a seed is persisted with the order, so changing what it produces makes every past "
            + "basket irreproducible");

    [Fact]
    public void The_Same_Seed_Replays_Exactly() =>
        Take(seed: 20261107, count: 32).Should().Equal(Take(seed: 20261107, count: 32));

    [Fact]
    public void Different_Seeds_Diverge_Immediately() =>
        Take(seed: 1, count: 4).Should().NotEqual(Take(seed: 2, count: 4));

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Every_Seed_Produces_Output_Including_The_Awkward_Ones(long seed) =>
        // Zero and the extremes are where a hand-written mixer tends to collapse to a constant.
        Take(seed, count: 8).Distinct().Should().HaveCountGreaterThan(1);

    [Fact]
    public void Next_Stays_Inside_Its_Range()
    {
        var random = new SplitMix64(7);

        for (var i = 0; i < 1000; i++)
        {
            random.Next(5, 9).Should().BeInRange(5, 8, "the upper bound is exclusive");
        }
    }

    [Fact]
    public void Next_Covers_Its_Whole_Range()
    {
        var random = new SplitMix64(99);
        var seen = new HashSet<int>();

        for (var i = 0; i < 1000; i++) seen.Add(random.Next(0, 4));

        // A generator that never returned one value would make a slice of the catalogue
        // unreachable, which no range check would notice.
        seen.Should().BeEquivalentTo([0, 1, 2, 3]);
    }

    [Fact]
    public void A_Single_Value_Range_Is_Refused_Rather_Than_Looping()
    {
        var random = new SplitMix64(1);
        var act = () => random.Next(3, 3);

        act.Should().Throw<ArgumentOutOfRangeException>(
            "an empty range is a caller bug, and modulo zero would be worse");
    }

    private static List<ulong> Take(long seed, int count)
    {
        var random = new SplitMix64(seed);
        var values = new List<ulong>(count);

        for (var i = 0; i < count; i++) values.Add(random.NextULong());

        return values;
    }
}
