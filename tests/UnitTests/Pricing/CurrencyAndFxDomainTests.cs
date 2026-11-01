using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Pricing;

/// <summary>
/// Currencies and exchange rates. Nearly everything refused here would otherwise become a wrong
/// number on an invoice, which is the one kind of bug a customer notices before we do.
/// </summary>
public sealed class CurrencyAndFxDomainTests
{
    private static readonly DateTimeOffset T0 = TestTime.Now;
    private static readonly DateOnly RateDay = DateOnly.FromDateTime(TestTime.Now.UtcDateTime);

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData(" sgd ", "SGD")]
    [InlineData("EUR", "EUR")]
    public void Currency_Codes_Are_Normalized(string input, string expected) =>
        Currency.Create(840, input).Code.Should().Be(expected);

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("US1")]
    [InlineData("")]
    [InlineData("   ")]
    public void Currency_Rejects_A_Code_That_Is_Not_Three_Letters(string code) =>
        FluentActions.Invoking(() => Currency.Create(840, code))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("CURRENCY_CODE_INVALID",
                "a wrong currency code makes every amount beside it meaningless");

    [Fact]
    public void A_Currency_Starts_Enabled_With_Two_Minor_Units()
    {
        var currency = Currency.Create(840, "USD");

        currency.IsEnabled.Should().BeTrue();
        currency.MinorUnits.Should().Be(2);
        currency.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void SetEnabled_To_The_Current_Value_Changes_Nothing()
    {
        var currency = Currency.Create(840, "USD");

        currency.SetEnabled(true, T0);

        currency.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void SetEnabled_Flips_And_Stamps()
    {
        var currency = Currency.Create(840, "USD");

        currency.SetEnabled(false, T0);

        currency.IsEnabled.Should().BeFalse();
        currency.UpdatedAt.Should().Be(T0);
    }

    [Fact]
    public void FxRate_Rejects_Non_Positive_Rate() =>
        FluentActions.Invoking(() => FxRate.Record(1, "USD", "SGD", 0m, "ecb", RateDay, T0))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("FX_RATE_INVALID");

    [Fact]
    public void FxRate_Rejects_Same_Base_And_Quote() =>
        FluentActions.Invoking(() => FxRate.Record(1, "USD", "usd", 1m, "ecb", RateDay, T0))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("FX_PAIR_INVALID",
                "the pair is compared after normalisation, or 'USD'/'usd' would slip through");

    [Fact]
    public void FxRate_Requires_A_Provider() =>
        FluentActions.Invoking(() => FxRate.Record(1, "USD", "SGD", 1.35m, "  ", RateDay, T0))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("FX_PROVIDER_REQUIRED");

    [Fact]
    public void FxRate_Keeps_The_Day_It_Is_For_Apart_From_The_Day_It_Was_Fetched()
    {
        var yesterday = RateDay.AddDays(-1);

        var rate = FxRate.Record(1, "USD", "SGD", 1.35m, "ecb", yesterday, T0);

        rate.RateDate.Should().Be(yesterday);
        rate.FetchedAt.Should().Be(T0);
    }

    [Fact]
    public void Override_Effective_Until_Exclusive_ValidUntil()
    {
        var validUntil = T0.AddDays(1);
        var @override = FxRateOverride.Create(1, "USD", "SGD", 1.40m, Guid.NewGuid(), T0, validUntil);

        @override.IsEffectiveAt(validUntil).Should().BeFalse(
            "at the instant an admin typed, the override has stopped");
        @override.IsEffectiveAt(validUntil.AddTicks(-1)).Should().BeTrue();
    }

    [Fact]
    public void An_Override_With_No_Expiry_Applies_Until_It_Is_Withdrawn()
    {
        var @override = FxRateOverride.Create(1, "USD", "SGD", 1.40m, Guid.NewGuid(), T0, null);

        @override.IsEffectiveAt(T0.AddYears(5)).Should().BeTrue();

        @override.Deactivate(T0.AddDays(1));

        @override.IsEffectiveAt(T0.AddDays(2)).Should().BeFalse();
    }

    [Fact]
    public void An_Already_Expired_Override_Is_Refused() =>
        FluentActions.Invoking(() => FxRateOverride.Create(
                1, "USD", "SGD", 1.40m, Guid.NewGuid(), T0, T0))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("FX_OVERRIDE_EXPIRY_INVALID",
                "setting an override that is already over is almost certainly a mistyped date");

    [Fact]
    public void Replace_Revives_A_Withdrawn_Override()
    {
        var @override = FxRateOverride.Create(1, "USD", "SGD", 1.40m, Guid.NewGuid(), T0, null);
        @override.Deactivate(T0.AddDays(1));
        var admin = Guid.NewGuid();

        @override.Replace(1.45m, admin, T0.AddDays(2), null);

        @override.IsActive.Should().BeTrue(
            "setting a rate is the decision to apply it; leaving the row inactive would be a "
            + "second, silent step");
        @override.Rate.Should().Be(1.45m);
        @override.SetBy.Should().Be(admin);
        @override.SetAt.Should().Be(T0.AddDays(2));
    }

    [Fact]
    public void Replace_Rejects_A_Non_Positive_Rate()
    {
        var @override = FxRateOverride.Create(1, "USD", "SGD", 1.40m, Guid.NewGuid(), T0, null);

        FluentActions.Invoking(() => @override.Replace(-1m, Guid.NewGuid(), T0, null))
            .Should().Throw<DomainException>().Which.Code.Should().Be("FX_RATE_INVALID");

        @override.Rate.Should().Be(1.40m, "a refused replacement leaves the override as it was");
    }

    [Fact]
    public void Deactivating_Twice_Changes_Nothing()
    {
        var @override = FxRateOverride.Create(1, "USD", "SGD", 1.40m, Guid.NewGuid(), T0, null);
        @override.Deactivate(T0.AddDays(1));

        @override.Deactivate(T0.AddDays(2));

        @override.SetAt.Should().Be(T0.AddDays(1));
    }

    [Fact]
    public void An_Override_Pair_Is_Normalized_And_Checked_Like_A_Rate()
    {
        FluentActions.Invoking(() => FxRateOverride.Create(
                1, "usd", "USD", 1.40m, Guid.NewGuid(), T0, null))
            .Should().Throw<DomainException>().Which.Code.Should().Be("FX_PAIR_INVALID");

        FxRateOverride.Create(1, "usd", "sgd", 1.40m, Guid.NewGuid(), T0, null)
            .Base.Should().Be("USD");
    }
}
