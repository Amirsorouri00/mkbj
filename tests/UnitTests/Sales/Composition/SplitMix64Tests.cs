using System.Reflection;
using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Composition;

/// <summary>
/// The PRNG whose output is, in effect, persisted.
/// </summary>
/// <remarks>
/// A composition's seed is stored with the order so the basket can be reproduced and explained.
/// That only works while the sequence a seed produces never changes — which is why
/// <c>System.Random</c> is not used, and why this pins the published values rather than merely
/// checking that the numbers look random. A change to these constants would make every past
/// order irreproducible, silently.
/// <para>
/// The type is internal, so the tests reach it through reflection rather than widening it for
/// their own convenience.
/// </para>
/// </remarks>
public sealed class SplitMix64Tests
{
    private static readonly Type Type =
        typeof(EwaysVNext.Modules.Sales.Application.Composition.CompositionEngine).Assembly
            .GetType("EwaysVNext.Modules.Sales.Application.Composition.SplitMix64", throwOnError: true)!;

    /// <summary>The reference outputs for seed 42, from the published algorithm.</summary>
    private static readonly ulong[] ExpectedForSeed42 =
    [
        0xBDD732262FEB6E95UL,
        0x28EFE333B266F103UL,
        0x47526757130F9F52UL,
    ];

    [Fact]
    public void The_Sequence_For_A_Seed_Never_Changes()
    {
        var produced = Take(seed: 42, count: ExpectedForSeed42.Length);

        produced.Should().Equal(ExpectedForSeed42,
            "a seed is persisted with the order, so changing what it produces makes every past "
            + "basket irreproducible");
    }

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
        var instance = Create(seed: 7);
        var next = Type.GetMethod("Next", BindingFlags.Public | BindingFlags.Instance)!;

        for (var i = 0; i < 1000; i++)
        {
            var value = (int)next.Invoke(instance, [5, 9])!;
            value.Should().BeInRange(5, 8, "the upper bound is exclusive");
        }
    }

    [Fact]
    public void Next_Covers_Its_Whole_Range()
    {
        var instance = Create(seed: 99);
        var next = Type.GetMethod("Next", BindingFlags.Public | BindingFlags.Instance)!;

        var seen = new HashSet<int>();
        for (var i = 0; i < 1000; i++) seen.Add((int)next.Invoke(instance, [0, 4])!);

        // A generator that never returns one value would make a whole slice of the catalogue
        // unreachable, which no range check would notice.
        seen.Should().BeEquivalentTo([0, 1, 2, 3]);
    }

    private static object Create(long seed) => Activator.CreateInstance(Type, seed)!;

    private static List<ulong> Take(long seed, int count)
    {
        var instance = Create(seed);
        var nextULong = Type.GetMethod("NextULong", BindingFlags.Public | BindingFlags.Instance)!;

        var values = new List<ulong>(count);

        // Boxed, so the struct's state has to be read back each time rather than mutating a copy.
        for (var i = 0; i < count; i++) values.Add((ulong)nextULong.Invoke(instance, null)!);

        return values;
    }
}
