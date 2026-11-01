using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Pricing.Application;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Outcomes;
using Xunit;

namespace EwaysVNext.UnitTests.Pricing;

/// <summary>
/// Turning a stored price into the number a customer sees. The rounding cases are the point:
/// this is the last rounding before an amount becomes money.
/// </summary>
public sealed class PriceResolverTests
{
    private static readonly DateTimeOffset T0 = TestTime.Now;
    private static readonly PriceProvenance Prov = PriceProvenance.Manual;

    private static Result<FxResolution> Fx(decimal rate) =>
        Result.Success(new FxResolution(
            rate,
            PriceSource.Fx,
            new FxSnapshot(7, null, "EUR", "USD", rate, T0, "ecb"),
            T0.AddHours(72)));

    private static Result<FxResolution> Unavailable() =>
        Result.Failure<FxResolution>(PricingErrors.FxRateUnavailable);

    private static VariantPrice Price(long amountMinor, string currency = "EUR") =>
        VariantPrice.Create(1, 100, amountMinor, currency, Prov, T0);

    [Fact]
    public void Override_Wins_Over_Fx()
    {
        var price = Price(10000);
        price.SetOverride(2, "SGD", 15000, T0);

        var quote = PriceResolver.Resolve(
            price, "SGD",
            (_, _) => throw new InvalidOperationException("no FX needed"), out _)!;

        quote.ReferenceMinor.Should().Be(15000);
        quote.Source.Should().Be(PriceSource.Override);
        quote.Fx.Should().BeNull("there was no conversion to record");
        (quote.LoMinor, quote.HiMinor).Should().Be((14250L, 15750L));
    }

    [Fact]
    public void Converted_Price_Rounds_Half_Even_In_Minor_Units()
    {
        var price = Price(125);    // 1.25 EUR

        PriceResolver.Resolve(price, "USD", (_, _) => Fx(1.1m), out _)!
            .ReferenceMinor.Should().Be(138, "137.5 rounds to the even 138");
        PriceResolver.Resolve(price, "USD", (_, _) => Fx(1.3m), out _)!
            .ReferenceMinor.Should().Be(162, "162.5 rounds to the even 162");
    }

    [Fact]
    public void Base_Currency_Needs_No_Rate()
    {
        var quote = PriceResolver.Resolve(
            Price(999, "USD"), "USD",
            (_, _) => throw new InvalidOperationException("no FX needed"), out _)!;

        quote.Source.Should().Be(PriceSource.Base);
        quote.ReferenceMinor.Should().Be(999);
        quote.Fx.Should().BeNull();
    }

    [Fact]
    public void Sub_Minor_Result_Is_Price_Missing()
    {
        var quote = PriceResolver.Resolve(Price(1, "USD"), "EUR", (_, _) => Fx(0.4m), out var error);

        quote.Should().BeNull();
        error!.Code.Should().Be("PRICE_MISSING",
            "rounding to zero would put a free item in the catalogue");
    }

    [Fact]
    public void Exactly_One_Minor_Unit_Is_Still_A_Price()
    {
        var quote = PriceResolver.Resolve(Price(2, "USD"), "EUR", (_, _) => Fx(0.5m), out var error);

        quote!.ReferenceMinor.Should().Be(1, "the floor is one minor unit, not two");
        error.Should().BeNull();
    }

    [Fact]
    public void An_Unavailable_Rate_Is_Reported_Rather_Than_Guessed()
    {
        var quote = PriceResolver.Resolve(Price(1000), "GBP", (_, _) => Unavailable(), out var error);

        quote.Should().BeNull();
        error!.Code.Should().Be("FX_RATE_UNAVAILABLE");
        error.Retryable.Should().BeTrue();
    }

    [Fact]
    public void The_Converted_Quote_Carries_The_Rate_It_Used()
    {
        var quote = PriceResolver.Resolve(Price(10000), "USD", (_, _) => Fx(1.0812m), out _)!;

        quote.ReferenceMinor.Should().Be(10812);
        quote.Source.Should().Be(PriceSource.Fx);
        quote.Fx!.FxRateId.Should().Be(7,
            "a price has to be explainable months later, and the rate is how");
    }

    [Fact]
    public void The_Band_Travels_With_The_Quote()
    {
        var quote = PriceResolver.Resolve(Price(10000), "USD", (_, _) => Fx(1.0812m), out _)!;

        quote.LoMinor.Should().Be(10272);
        quote.HiMinor.Should().Be(11352);
    }

    [Fact]
    public void The_Resolver_Asks_For_The_Pair_It_Actually_Needs()
    {
        string? askedBase = null;
        string? askedQuote = null;

        PriceResolver.Resolve(Price(1000, "EUR"), "sgd", (@base, quote) =>
        {
            askedBase = @base;
            askedQuote = quote;
            return Fx(1.5m);
        }, out _);

        askedBase.Should().Be("EUR");
        askedQuote.Should().Be("SGD", "the asked-for currency is normalised before anything uses it");
    }

    [Fact]
    public void An_Override_For_A_Different_Currency_Does_Not_Apply()
    {
        var price = Price(10000);
        price.SetOverride(2, "SGD", 15000, T0);

        var quote = PriceResolver.Resolve(price, "USD", (_, _) => Fx(1.1m), out _)!;

        quote.Source.Should().Be(PriceSource.Fx);
        quote.ReferenceMinor.Should().Be(11000);
    }

    [Fact]
    public void Setting_An_Override_Twice_Replaces_It()
    {
        var price = Price(10000);
        price.SetOverride(2, "SGD", 15000, T0);
        price.SetOverride(3, "SGD", 16000, T0.AddHours(1));

        price.Overrides.Should().ContainSingle()
            .Which.AmountMinor.Should().Be(16000, "there is only ever one row per currency");
    }

    [Fact]
    public void Removing_An_Override_Puts_The_Currency_Back_On_Conversion()
    {
        var price = Price(10000);
        price.SetOverride(2, "SGD", 15000, T0);

        price.RemoveOverride("sgd", T0.AddHours(1)).Should().BeTrue();
        price.RemoveOverride("SGD", T0.AddHours(2)).Should().BeFalse(
            "a caller clearing an override it is unsure about should not have to catch an exception");

        PriceResolver.Resolve(price, "SGD", (_, _) => Fx(1.5m), out _)!
            .Source.Should().Be(PriceSource.Fx);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_Non_Positive_Price_Is_Refused(long amountMinor) =>
        FluentActions.Invoking(() => Price(amountMinor))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("PRICE_AMOUNT_INVALID");

    [Fact]
    public void A_Non_Positive_Override_Is_Refused() =>
        FluentActions.Invoking(() => Price(10000).SetOverride(2, "SGD", 0, T0))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("PRICE_AMOUNT_INVALID");

    [Fact]
    public void SetBase_Replaces_Price_And_Provenance_Together()
    {
        var price = Price(10000);
        var observed = new PriceProvenance(
            42, "https://example/kettle", T0, 99.95m, "EUR", TaxIncluded: true);

        price.SetBase(9995, "eur", observed, T0.AddHours(1));

        price.BaseAmountMinor.Should().Be(9995);
        price.BaseCurrency.Should().Be("EUR");
        price.Provenance.Should().Be(observed,
            "a new price with the old provenance would cite an observation of something else");
        price.UpdatedAt.Should().Be(T0.AddHours(1));
    }

    [Fact]
    public void SetBase_To_The_Same_Values_Changes_Nothing()
    {
        var price = Price(10000);

        price.SetBase(10000, "EUR", Prov, T0.AddHours(1));

        price.UpdatedAt.Should().Be(T0);
    }
}
