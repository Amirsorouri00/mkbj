using AwesomeAssertions;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using Xunit;

namespace EwaysVNext.UnitTests.Pricing;

/// <summary>
/// Which rate a quote uses, and why. Every case here is one an invoice could later be disputed
/// over, so the snapshot is asserted alongside the number.
/// </summary>
public sealed class FxRateResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 16, 0, 0, TimeSpan.Zero);

    private static FxRate Eur(string quote, decimal rate, DateTimeOffset fetchedAt, long id = 1) =>
        FxRate.Record(id, "EUR", quote, rate, "ecb", DateOnly.FromDateTime(fetchedAt.UtcDateTime), fetchedAt);

    private static FxRateBook Book(params FxRate[] rates) => new(rates, [], TimeSpan.FromHours(72));

    [Fact]
    public void Same_Currency_Needs_No_Rate_At_All()
    {
        var resolution = FxRateResolver.Resolve(Book(), "USD", "usd", Now).Value!;

        resolution.Rate.Should().Be(1m);
        resolution.Source.Should().Be(PriceSource.Base);
        resolution.Snapshot.Should().BeNull();
        resolution.ValidUntil.Should().BeNull();
    }

    [Fact]
    public void Rate_Age_Boundary_Is_Inclusive()
    {
        FxRateResolver.Resolve(Book(Eur("USD", 1.08m, Now.AddHours(-72))), "EUR", "USD", Now)
            .IsSuccess.Should().BeTrue("the limit is how stale is tolerable, not one tick less");

        var stale = FxRateResolver.Resolve(
            Book(Eur("USD", 1.08m, Now.AddHours(-72).AddSeconds(-1))), "EUR", "USD", Now);

        stale.Error!.Code.Should().Be("FX_RATE_UNAVAILABLE");
        stale.Error.Retryable.Should().BeTrue(
            "the refresh job will almost certainly fix it, so a caller may try again");
    }

    [Fact]
    public void Aed_Without_Usd_Rate_Is_Unavailable()
    {
        var book = Book(Eur("GBP", 0.85m, Now.AddHours(-1)));

        FxRateResolver.Resolve(book, "EUR", "AED", Now).Error!.Code.Should().Be("FX_RATE_UNAVAILABLE");
        FxRateResolver.Resolve(book, "GBP", "AED", Now).Error!.Code.Should().Be("FX_RATE_UNAVAILABLE");
    }

    [Fact]
    public void Usd_To_Aed_Uses_Peg_Without_Any_Provider_Rate()
    {
        var resolution = FxRateResolver.Resolve(Book(), "USD", "AED", Now).Value!;

        resolution.Rate.Should().Be(3.6725m);
        resolution.Snapshot!.Provenance.Should().Be("derived:usd-peg");
        resolution.Snapshot.FxRateId.Should().BeNull("no row produced it");
        resolution.ValidUntil.Should().BeNull("a peg does not go stale");
    }

    [Fact]
    public void Aed_To_Usd_Is_The_Peg_Inverted_And_Rounded()
    {
        var resolution = FxRateResolver.Resolve(Book(), "AED", "USD", Now).Value!;

        resolution.Rate.Should().Be(Math.Round(1m / 3.6725m, 10, MidpointRounding.ToEven));
        resolution.Snapshot!.Provenance.Should().Be("derived:usd-peg");
    }

    [Fact]
    public void Eur_To_Aed_Is_Usd_Leg_Times_Peg()
    {
        var resolution = FxRateResolver.Resolve(
            Book(Eur("USD", 1.0812m, Now.AddHours(-1), id: 7)), "EUR", "AED", Now).Value!;

        resolution.Rate.Should().Be(3.9707070000m);        // 1.0812 × 3.6725
        resolution.Snapshot!.FxRateId.Should().Be(7);
        resolution.Snapshot.Provenance.Should().Be("ecb:cross-eur+derived:usd-peg");
    }

    [Fact]
    public void Cross_Rate_Goes_Through_Eur_And_Rounds_To_Ten_Places()
    {
        var book = Book(
            Eur("USD", 1.0812m, Now.AddHours(-2), id: 1),
            Eur("SGD", 1.4321m, Now.AddHours(-1), id: 2));

        var resolution = FxRateResolver.Resolve(book, "USD", "SGD", Now).Value!;

        resolution.Rate.Should().Be(1.3245467999m);        // round10(1.4321 / 1.0812), half-even
        resolution.Snapshot!.RateAt.Should().Be(Now.AddHours(-2),
            "a cross rate is only as current as its stalest leg");
        resolution.Snapshot.FxRateId.Should().Be(2, "the quote's leg identifies the conversion");
        resolution.Snapshot.Provenance.Should().Be("ecb:cross-eur");
        resolution.ValidUntil.Should().Be(Now.AddHours(70));
    }

    [Fact]
    public void A_Single_Direct_Leg_Is_Plain_Ecb()
    {
        var resolution = FxRateResolver.Resolve(
            Book(Eur("USD", 1.0812m, Now.AddHours(-1), id: 3)), "EUR", "USD", Now).Value!;

        resolution.Snapshot!.Provenance.Should().Be("ecb");
        resolution.Snapshot.FxRateId.Should().Be(3);
    }

    [Fact]
    public void Converting_Into_The_Hub_Points_At_The_Base_Leg()
    {
        var resolution = FxRateResolver.Resolve(
            Book(Eur("USD", 1.0812m, Now.AddHours(-1), id: 4)), "USD", "EUR", Now).Value!;

        resolution.Rate.Should().Be(Math.Round(1m / 1.0812m, 10, MidpointRounding.ToEven));
        resolution.Snapshot!.FxRateId.Should().Be(4,
            "the quote is the hub, so there is no row for it to point at but the base's");
        resolution.Snapshot.Provenance.Should().Be("ecb");
    }

    [Fact]
    public void Effective_Override_Beats_Fresh_Provider_And_Expired_Override_Does_Not()
    {
        var @override = FxRateOverride.Create(
            9, "EUR", "USD", 1.2m, Guid.NewGuid(), Now.AddDays(-1), validUntil: Now.AddHours(1));
        var book = new FxRateBook([Eur("USD", 1.08m, Now.AddHours(-1))], [@override], TimeSpan.FromHours(72));

        FxRateResolver.Resolve(book, "EUR", "USD", Now).Value!.Snapshot!.FxOverrideId.Should().Be(9);
        FxRateResolver.Resolve(book, "EUR", "USD", Now.AddHours(1)).Value!.Rate.Should().Be(1.08m);
    }

    [Fact]
    public void Override_Is_Used_Even_When_Provider_Is_Stale() =>
        FxRateResolver.Resolve(
            new FxRateBook(
                [],
                [FxRateOverride.Create(9, "EUR", "GBP", 0.9m, Guid.NewGuid(), Now, null)],
                TimeSpan.FromHours(72)),
            "EUR", "GBP", Now).Value!.Rate.Should().Be(0.9m);

    [Fact]
    public void An_Override_Beats_The_Peg_Too()
    {
        var @override = FxRateOverride.Create(11, "USD", "AED", 3.70m, Guid.NewGuid(), Now, null);
        var book = new FxRateBook([], [@override], TimeSpan.FromHours(72));

        var resolution = FxRateResolver.Resolve(book, "USD", "AED", Now).Value!;

        resolution.Rate.Should().Be(3.70m,
            "an override is a decision; the peg is the default it is overriding");
        resolution.Snapshot!.FxOverrideId.Should().Be(11);
    }

    [Fact]
    public void An_Override_For_The_Other_Direction_Is_Not_Used()
    {
        var @override = FxRateOverride.Create(12, "USD", "EUR", 0.8m, Guid.NewGuid(), Now, null);
        var book = new FxRateBook(
            [Eur("USD", 1.08m, Now.AddHours(-1), id: 5)], [@override], TimeSpan.FromHours(72));

        var resolution = FxRateResolver.Resolve(book, "EUR", "USD", Now).Value!;

        resolution.Snapshot!.FxOverrideId.Should().BeNull(
            "inverting an override would invent a rate the admin never approved");
        resolution.Rate.Should().Be(1.08m);
    }

    [Fact]
    public void The_Override_Snapshot_Records_When_It_Was_Set_Rather_Than_Now()
    {
        var setAt = Now.AddDays(-3);
        var @override = FxRateOverride.Create(13, "EUR", "GBP", 0.9m, Guid.NewGuid(), setAt, null);
        var book = new FxRateBook([], [@override], TimeSpan.FromHours(72));

        var resolution = FxRateResolver.Resolve(book, "EUR", "GBP", Now).Value!;

        resolution.Snapshot!.RateAt.Should().Be(setAt);
        resolution.ValidUntil.Should().BeNull("this override has no expiry");
    }

    [Fact]
    public void Aed_Cross_Through_Two_Legs_Still_Reports_The_Peg()
    {
        var book = Book(
            Eur("USD", 1.0812m, Now.AddHours(-3), id: 1),
            Eur("SGD", 1.4321m, Now.AddHours(-1), id: 2));

        var resolution = FxRateResolver.Resolve(book, "SGD", "AED", Now).Value!;

        resolution.Snapshot!.Provenance.Should().Be("ecb:cross-eur+derived:usd-peg");
        resolution.Snapshot.RateAt.Should().Be(Now.AddHours(-3), "the dollar leg is the older one");
    }

    [Fact]
    public void A_Missing_Base_Leg_Is_As_Unavailable_As_A_Missing_Quote_Leg()
    {
        var book = Book(Eur("SGD", 1.4321m, Now.AddHours(-1)));

        FxRateResolver.Resolve(book, "USD", "SGD", Now).Error!.Code
            .Should().Be("FX_RATE_UNAVAILABLE");
    }

    [Fact]
    public void A_Stale_Row_Is_Ignored_In_Favour_Of_Nothing_Rather_Than_Used()
    {
        // Two rows for one pair, the fresher one stale as well: the resolver must not fall back
        // to the older one just because it exists.
        var book = new FxRateBook(
            [
                Eur("USD", 1.08m, Now.AddHours(-100), id: 1),
                Eur("USD", 1.09m, Now.AddHours(-80), id: 2),
            ],
            [],
            TimeSpan.FromHours(72));

        FxRateResolver.Resolve(book, "EUR", "USD", Now).Error!.Code
            .Should().Be("FX_RATE_UNAVAILABLE");
    }

    [Fact]
    public void The_Newest_Fresh_Row_Wins_When_Several_Are_Present()
    {
        var book = new FxRateBook(
            [
                Eur("USD", 1.08m, Now.AddHours(-10), id: 1),
                Eur("USD", 1.09m, Now.AddHours(-1), id: 2),
            ],
            [],
            TimeSpan.FromHours(72));

        var resolution = FxRateResolver.Resolve(book, "EUR", "USD", Now).Value!;

        resolution.Rate.Should().Be(1.09m);
        resolution.Snapshot!.FxRateId.Should().Be(2);
    }
}
