using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Tools.CompositionSimulator;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace EwaysVNext.UnitTests.Sales.Composition;

/// <summary>
/// Properties that must hold for every amount, not just the ones someone thought to write down.
/// </summary>
/// <remarks>
/// <para>
/// The mandated amounts prove the engine handles the cases the product requires. These prove it
/// cannot do something catastrophic on an amount nobody anticipated — specifically, that it
/// never returns a basket which does not total what was asked for. A composition engine that
/// occasionally sells the wrong amount is worse than one that occasionally declines.
/// </para>
/// <para>
/// The engine itself is deterministic, so a failing case here is reproducible from the inputs
/// FsCheck prints; the randomness is in which inputs get tried, not in what they do.
/// </para>
/// </remarks>
public sealed class CompositionPropertyTests
{
    private const int MaxLines = 200;

    private static EwaysVNext.Outcomes.Result<CompositionPlan> Compose(long target, string currency, long seed) =>
        CompositionEngine.Compose(
            target,
            SeedLikeCatalog.For(currency),
            new CompositionOptions(MaxLines, SeedLikeCatalog.ToUsdMinor(target, currency)),
            seed,
            new IterationBudget(200_000));

    /// <summary>
    /// Either it declines for one of two reasons, or the basket is genuinely correct.
    /// </summary>
    /// <remarks>
    /// The failure codes are part of the property: an unexpected code would mean the engine
    /// failed in a way the API contract does not describe, and the partner would get a status
    /// their client has no branch for.
    /// </remarks>
    [Property(MaxTest = 300)]
    public Property Every_Success_Is_Exact_Within_Band_With_Positive_Quantities(
        PositiveInt cents, long seed)
    {
        var target = 1_000L + (cents.Get % 99_999_000L);
        var pool = SeedLikeCatalog.For("USD");
        var result = Compose(target, "USD", seed);

        var holds = result.IsFailure
            ? result.Error!.Code is "EXACT_COMPOSITION_UNAVAILABLE" or "COMPOSITION_TIMEOUT"
            : CompositionVerifier.Verify(target, pool, result.Value!.Lines, MaxLines).IsSuccess
              && result.Value.Lines.Sum(line => line.LineTotalMinor) == target
              && result.Value.Lines.All(line => line.Quantity >= 1);

        return holds.ToProperty();
    }

    [Property(MaxTest = 100)]
    public bool Same_Seed_Same_Lines(PositiveInt cents, long seed)
    {
        var target = 1_000L + (cents.Get % 9_999_000L);

        var first = Compose(target, "EUR", seed);
        var second = Compose(target, "EUR", seed);

        return first.IsSuccess == second.IsSuccess
            && (first.IsFailure || first.Value!.Lines.SequenceEqual(second.Value!.Lines));
    }

    /// <summary>
    /// The same holds for catalogues nothing like the demo one.
    /// </summary>
    /// <remarks>
    /// The seeded catalogue is one shape. A real seller's could be ten near-identical prices, or
    /// one cheap item and one enormous one — shapes where a heuristic search is most likely to
    /// talk itself into a basket that does not add up.
    /// </remarks>
    [Property(MaxTest = 300)]
    public bool Arbitrary_Pools_Never_Produce_An_Invalid_Success(
        NonEmptyArray<PositiveInt> references, PositiveInt amount, long seed)
    {
        var pool = references.Get
            .Select((reference, index) =>
            {
                var value = 50 + (reference.Get % 1_000_000);
                return new CompositionCandidate(
                    index + 1, index % 5, value, ((value * 95L) + 99) / 100, value * 105L / 100);
            })
            .ToArray();

        var target = 100L + (amount.Get % 5_000_000L);

        var result = CompositionEngine.Compose(
            target, pool, new CompositionOptions(MaxLines, target), seed, new IterationBudget(50_000));

        return result.IsFailure
            || CompositionVerifier.Verify(target, pool, result.Value!.Lines, MaxLines).IsSuccess;
    }

    /// <summary>
    /// An amount a single product could be sold at must never be declined.
    /// </summary>
    /// <remarks>
    /// This is the one place the engine makes a positive promise rather than a safety one: if
    /// some variant's band contains the whole amount, a one-line basket exists and the engine
    /// has to find it. Declining here would be refusing an order that is trivially fillable.
    /// </remarks>
    [Property(MaxTest = 200)]
    public bool A_Candidate_Whose_Band_Contains_The_Target_Guarantees_Success(
        PositiveInt index, long seed)
    {
        var pool = SeedLikeCatalog.For("SGD");
        var candidate = pool[index.Get % pool.Count];
        var target = candidate.LoMinor + (index.Get % (candidate.HiMinor - candidate.LoMinor + 1));

        return Compose(target, "SGD", seed).IsSuccess;
    }
}
