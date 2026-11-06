using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Composition;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Composition;

/// <summary>
/// The last check before a partner is billed.
/// </summary>
/// <remarks>
/// The verifier shares no code with the engine, so these tests are about one question: does it
/// actually refuse every basket that breaks a rule? A verifier that passes a bad basket is worse
/// than no verifier, because the engine's output would then carry a stamp of approval.
/// </remarks>
public sealed class CompositionVerifierTests
{
    private const int MaxLines = 200;

    /// <summary>Variant 1 at 10.00 ±5%, variant 2 at 20.00 ±5%.</summary>
    private static readonly CompositionCandidate[] Pool =
    [
        new(VariantId: 1, CategoryId: 10, ReferenceMinor: 1000, LoMinor: 950, HiMinor: 1050),
        new(VariantId: 2, CategoryId: 11, ReferenceMinor: 2000, LoMinor: 1900, HiMinor: 2100),
    ];

    [Fact]
    public void Accepts_A_Basket_That_Totals_Exactly()
    {
        ComposedLine[] lines = [new(1, 10, 2, 1000, 1000), new(2, 11, 1, 2000, 2000)];

        Verify(4000, lines).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Accepts_Band_Edges()
    {
        // Both ends of the band are legal prices, and a verifier that treated the band as
        // exclusive would reject baskets the engine is entitled to build.
        ComposedLine[] lines = [new(1, 10, 1, 950, 1000), new(2, 11, 2, 2100, 2000)];

        Verify(950 + (2 * 2100), lines).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BadBaskets))]
    public void Rejects(string because, long target, ComposedLine[] lines)
    {
        var result = Verify(target, lines);

        result.IsFailure.Should().BeTrue(because);
        result.Error!.Code.Should().Be("COMPOSITION_VERIFICATION_FAILED");

        // 500, not 4xx: the caller did nothing wrong. A basket that fails here is this system's
        // bug, and the partner must not be billed for it.
        result.Error.HttpStatus.Should().Be(500);
        result.Error.Retryable.Should().BeFalse();
    }

    public static TheoryData<string, long, ComposedLine[]> BadBaskets() => new()
    {
        {
            "one minor unit short is still the wrong amount — that is the whole promise",
            3999, [new(1, 10, 2, 1000, 1000), new(2, 11, 1, 2000, 2000)]
        },
        {
            "one minor unit over",
            4001, [new(1, 10, 2, 1000, 1000), new(2, 11, 1, 2000, 2000)]
        },
        {
            "a cent below the band floor",
            949, [new(1, 10, 1, 949, 1000)]
        },
        {
            "a cent above the band ceiling",
            1051, [new(1, 10, 1, 1051, 1000)]
        },
        {
            "a line of nothing is not a line",
            0, [new(1, 10, 0, 1000, 1000)]
        },
        {
            "a negative quantity would be a credit nobody asked for",
            -1000, [new(1, 10, -1, 1000, 1000)]
        },
        {
            "a variant that is not in the pool was never offered for sale",
            1000, [new(99, 10, 1, 1000, 1000)]
        },
        {
            "the same variant twice should have been one line with a larger quantity",
            2000, [new(1, 10, 1, 1000, 1000), new(1, 10, 1, 1000, 1000)]
        },
        {
            "an empty basket cannot total anything",
            0, []
        },
    };

    [Fact]
    public void Rejects_More_Lines_Than_Allowed()
    {
        // One candidate per line, all within band, totalling exactly — correct in every way
        // except the one being tested.
        var pool = Enumerable.Range(1, 5)
            .Select(id => new CompositionCandidate(id, 10, 1000, 950, 1050)).ToArray();
        var lines = pool.Select(c => new ComposedLine(c.VariantId, 10, 1, 1000, 1000)).ToArray();

        CompositionVerifier.Verify(5000, pool, lines, maxLines: 4)
            .IsFailure.Should().BeTrue();

        CompositionVerifier.Verify(5000, pool, lines, maxLines: 5)
            .IsSuccess.Should().BeTrue("five lines is not more than five");
    }

    [Fact]
    public void Does_Not_Trust_The_Line_Reference_Price()
    {
        // A line claiming a reference price the catalogue never had, but priced inside the real
        // band and totalling correctly. The verifier recomputes from the pool, so the lie about
        // the reference is irrelevant — and a verifier that banded against the claimed reference
        // would accept a line priced ±5% of a number the engine made up.
        ComposedLine[] honest = [new(1, 10, 1, 1000, ReferenceMinor: 999_999)];

        Verify(1000, honest).IsSuccess.Should().BeTrue();

        ComposedLine[] outOfBand = [new(1, 10, 1, 90_000, ReferenceMinor: 90_000)];

        Verify(90_000, outOfBand).IsFailure.Should().BeTrue(
            "the band comes from the pool, not from what the line claims its reference was");
    }

    [Fact]
    public void A_Quantity_Large_Enough_To_Overflow_Is_Caught_Rather_Than_Wrapped()
    {
        var pool = new[] { new CompositionCandidate(1, 10, long.MaxValue / 2, 1, long.MaxValue) };
        ComposedLine[] lines = [new(1, 10, int.MaxValue, long.MaxValue, long.MaxValue)];

        var act = () => CompositionVerifier.Verify(0, pool, lines, MaxLines);

        act.Should().Throw<OverflowException>(
            "a wrapped total could read as the requested amount, which is the one outcome this "
            + "check exists to prevent");
    }

    private static EwaysVNext.Outcomes.Result Verify(long target, ComposedLine[] lines) =>
        CompositionVerifier.Verify(target, Pool, lines, MaxLines);
}
