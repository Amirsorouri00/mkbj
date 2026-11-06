using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Tools.CompositionSimulator;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Composition;

/// <summary>
/// The engine that turns "I want to spend $12,845.99" into a basket of real SKUs.
/// </summary>
/// <remarks>
/// Two properties matter more than any particular basket: every success totals the amount
/// exactly and passes the independent verifier, and the same seed over the same pool always
/// produces the same basket. The second is what makes an order explainable a year later.
/// </remarks>
public sealed class CompositionEngineTests
{
    private const int MaxLines = 20;

    private static CompositionOptions Options(long targetMinor) => new(MaxLines, targetMinor);

    private static IterationBudget Budget(int reads = 500_000) => new(reads);

    /// <summary>
    /// Sixty candidates across twelve categories, prices spread over four orders of magnitude.
    /// </summary>
    /// <remarks>
    /// Shaped like the demo catalogue rather than a convenient set: the cheap end is where the
    /// band is narrowest in absolute terms and exact hits are hardest, so a pool of round
    /// hundreds would make the engine look better than it is.
    /// </remarks>
    private static CompositionCandidate[] Pool()
    {
        var candidates = new List<CompositionCandidate>(60);
        long[] references =
        [
            499, 1_299, 2_450, 3_999, 7_250, 11_999, 19_950, 29_999, 44_500, 65_000,
            89_999, 125_000, 174_999, 239_500, 325_000,
        ];

        var variantId = 1L;
        for (var category = 0; category < 12; category++)
        {
            for (var i = 0; i < 5; i++)
            {
                var reference = references[(category + (i * 3)) % references.Length] + (category * 7);
                candidates.Add(new CompositionCandidate(
                    variantId++,
                    CategoryId: 100 + category,
                    reference,
                    LoMinor: ((reference * 95) + 99) / 100,
                    HiMinor: reference * 105 / 100));
            }
        }

        return [.. candidates];
    }

    [Fact]
    public void An_Empty_Pool_Cannot_Compose_Anything()
    {
        var result = CompositionEngine.Compose(50_000, [], Options(50_000), 1, Budget());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("EXACT_COMPOSITION_UNAVAILABLE");
        result.Error.HttpStatus.Should().Be(422, "the request was understood and refused");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_Non_Positive_Target_Is_A_Validation_Failure(long target)
    {
        var result = CompositionEngine.Compose(target, Pool(), Options(1), 1, Budget());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        result.Error.HttpStatus.Should().Be(400);
    }

    [Fact]
    public void An_Exhausted_Deadline_Times_Out_Rather_Than_Reporting_Impossible()
    {
        var result = CompositionEngine.Compose(
            1_284_599, Pool(), Options(1_284_599), 1, new ExpiredBudget());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("COMPOSITION_TIMEOUT");
        result.Error.Retryable.Should().BeTrue(
            "a timeout says nothing about the amount, so trying again may well work");
    }

    [Fact]
    public void A_Single_Candidate_At_Its_Reference_Composes_One_Line()
    {
        CompositionCandidate[] pool = [new(1, 10, 1000, 950, 1050)];

        var plan = CompositionEngine.Compose(1000, pool, Options(1000), 1, Budget()).Value!;

        var line = plan.Lines.Should().ContainSingle().Which;
        line.VariantId.Should().Be(1);
        line.Quantity.Should().Be(1);
        line.UnitPriceMinor.Should().Be(1000);
    }

    [Fact]
    public void An_Unreachable_Target_Is_Reported_As_Unavailable()
    {
        // One fixed-price candidate: the reachable totals are multiples of 1000 and nothing else.
        CompositionCandidate[] pool = [new(1, 10, 1000, 1000, 1000)];

        var result = CompositionEngine.Compose(1500, pool, Options(1500), 1, Budget());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("EXACT_COMPOSITION_UNAVAILABLE");
        result.Error.Retryable.Should().BeFalse(
            "the same amount against the same catalogue will fail the same way");
    }

    /// <summary>
    /// The same seed over the same candidates always gives the same basket, whatever order the
    /// candidates arrived in.
    /// </summary>
    /// <remarks>
    /// Run against the shared seeded catalogue — 400 candidates over five decades — rather than
    /// the small hand-built pool, because input-order independence is easy to satisfy by
    /// accident when there are only sixty candidates and most attempts pick the same handful.
    /// </remarks>
    [Fact]
    public void Is_Deterministic_Per_Seed_And_Independent_Of_Input_Order()
    {
        var pool = SeedLikeCatalog.For("USD");

        var forwards = CompositionEngine.Compose(
            1_284_599, pool, Options(1_284_599), 7, Budget()).Value!;
        var backwards = CompositionEngine.Compose(
            1_284_599, [.. pool.Reverse()], Options(1_284_599), 7, Budget()).Value!;

        backwards.Lines.Should().Equal(forwards.Lines,
            "the pool is normalised, so the order a query happened to return it in cannot change "
            + "what the partner is sold");
        backwards.Stats.Seed.Should().Be(forwards.Stats.Seed);
    }

    [Fact]
    public void A_Different_Seed_Generally_Gives_A_Different_Basket()
    {
        var pool = Pool();

        var first = CompositionEngine.Compose(1_284_599, pool, Options(1_284_599), 1, Budget()).Value!;
        var second = CompositionEngine.Compose(1_284_599, pool, Options(1_284_599), 2, Budget()).Value!;

        // Not a guarantee the engine makes, but if it failed the seed would be doing nothing.
        second.Lines.Should().NotEqual(first.Lines);
    }

    [Theory]
    [InlineData(1_284_599L)]
    [InlineData(50_000L)]
    [InlineData(100_000L)]
    [InlineData(999_999L)]
    [InlineData(5_000_000L)]
    [InlineData(1_000L)]
    public void Every_Success_Totals_Exactly_And_Passes_The_Verifier(long target)
    {
        var pool = Pool();
        var result = CompositionEngine.Compose(target, pool, Options(target), 42, Budget());

        result.IsSuccess.Should().BeTrue(result.Error?.Message);

        var lines = result.Value!.Lines;
        lines.Sum(line => line.LineTotalMinor).Should().Be(target);

        CompositionVerifier.Verify(target, pool, lines, MaxLines)
            .IsSuccess.Should().BeTrue("the engine must never return a basket the verifier rejects");
    }

    [Fact]
    public void Lines_Come_From_Distinct_Categories_When_The_Catalogue_Allows()
    {
        var plan = CompositionEngine.Compose(
            1_284_599, Pool(), Options(1_284_599), 42, Budget()).Value!;

        plan.Lines.Select(line => line.CategoryId).Should().OnlyHaveUniqueItems(
            "twelve categories is more than the line count, so a repeat would be the engine not "
            + "trying");
    }

    [Fact]
    public void Stats_Carry_Enough_To_Reproduce_The_Basket()
    {
        var plan = CompositionEngine.Compose(
            1_284_599, Pool(), Options(1_284_599), 1234, Budget()).Value!;

        plan.Stats.Seed.Should().Be(1234);
        plan.Stats.StrategyVersion.Should().Be("v1");
        plan.Stats.CandidatePoolSize.Should().Be(60);
        plan.Stats.Iterations.Should().BeGreaterThan(0);
        plan.Stats.AttemptSummaries.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(20);
    }

    [Fact]
    public void A_Duplicated_Candidate_Is_Used_Once()
    {
        // The same variant arriving twice — a join that fanned out, say — must not become two
        // lines selling the same thing.
        CompositionCandidate[] pool =
        [
            new(1, 10, 1000, 950, 1050),
            new(1, 10, 1000, 950, 1050),
            new(2, 11, 2000, 1900, 2100),
        ];

        var plan = CompositionEngine.Compose(4000, pool, Options(4000), 3, Budget()).Value!;

        plan.Stats.CandidatePoolSize.Should().Be(2);
        plan.Lines.Select(line => line.VariantId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_Candidate_With_An_Impossible_Band_Is_Dropped()
    {
        CompositionCandidate[] pool =
        [
            new(1, 10, 1000, LoMinor: 1050, HiMinor: 950),   // inverted
            new(2, 11, 1000, LoMinor: 0, HiMinor: 1050),     // free
            new(3, 12, 2000, 1900, 2100),
        ];

        var plan = CompositionEngine.Compose(2000, pool, Options(2000), 5, Budget()).Value!;

        plan.Stats.CandidatePoolSize.Should().Be(1, "only the third candidate is sane");
        plan.Lines.Should().OnlyContain(line => line.VariantId == 3);
    }
}
