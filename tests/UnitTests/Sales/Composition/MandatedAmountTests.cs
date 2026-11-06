using AwesomeAssertions;
using EwaysVNext.Modules.Pricing.Application.Bands;
using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Modules.Sales.Application.Money;
using EwaysVNext.Tools.CompositionSimulator;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Composition;

/// <summary>
/// The amounts the product is required to be able to compose, in every currency it sells in.
/// </summary>
/// <remarks>
/// These are the acceptance criteria in test form: a partner asking for $17.43 or for
/// $1,000,000.00 must get a real basket, not an apology. If one of these fails, the engine's
/// heuristics are what change — never this file.
/// </remarks>
public sealed class MandatedAmountTests
{
    private const int MaxLines = 200;

    private static readonly long[] Amounts = [1_000, 1_743, 50_000, 1_284_599, 100_000_000];

    private static readonly long[] Seeds = [1, 2, 3];

    public static TheoryData<string, long> Mandated()
    {
        var data = new TheoryData<string, long>();

        foreach (var currency in SeedLikeCatalog.Currencies)
        {
            foreach (var amount in Amounts) data.Add(currency, amount);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Mandated))]
    public void Mandated_Amount_Composes_Exactly(string currency, long target)
    {
        var pool = SeedLikeCatalog.For(currency);
        var options = new CompositionOptions(MaxLines, SeedLikeCatalog.ToUsdMinor(target, currency));

        foreach (var seed in Seeds)
        {
            var result = CompositionEngine.Compose(
                target, pool, options, seed, new IterationBudget(200_000));

            result.IsSuccess.Should().BeTrue(
                $"{currency} {AmountText.Format(target)} seed {seed}: {result.Error?.Code}");

            var lines = result.Value!.Lines;

            lines.Sum(line => line.LineTotalMinor).Should().Be(target);
            CompositionVerifier.Verify(target, pool, lines, MaxLines).IsSuccess.Should().BeTrue();
        }
    }

    /// <summary>
    /// The stand-in catalogue bands exactly as Pricing does.
    /// </summary>
    /// <remarks>
    /// Without this the engine could be measured against a band the real shop never offers, and
    /// every mandated amount above would be evidence about the wrong system.
    /// </remarks>
    [Fact]
    public void SeedLikeCatalog_Band_Equals_PriceBand_For()
    {
        var mismatched = SeedLikeCatalog.Currencies
            .SelectMany(SeedLikeCatalog.For)
            .Where(candidate =>
            {
                var (lo, hi) = PriceBand.For(candidate.ReferenceMinor);
                return lo != candidate.LoMinor || hi != candidate.HiMinor;
            })
            .ToList();

        mismatched.Should().BeEmpty();
    }

    [Fact]
    public void The_Catalogue_Spans_The_Range_It_Claims_To()
    {
        var usd = SeedLikeCatalog.For("USD");

        usd.Should().HaveCount(400);
        usd.Select(candidate => candidate.CategoryId).Distinct().Should().HaveCount(12);

        usd.Min(candidate => candidate.ReferenceMinor).Should().BeLessThanOrEqualTo(100,
            "the cheap end is where a ±5% band is only a few cents wide, and that is the hard case");
        usd.Max(candidate => candidate.ReferenceMinor).Should().BeGreaterThanOrEqualTo(4_000_000);
    }

    [Fact]
    public void The_Catalogue_Is_The_Same_On_Every_Call() =>
        SeedLikeCatalog.For("SGD").Should().Equal(SeedLikeCatalog.For("SGD"));
}
