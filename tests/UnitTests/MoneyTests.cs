using AwesomeAssertions;
using Xunit;
using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;

namespace EwaysVNext.UnitTests;

public class MoneyTests
{
    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("CHF")]
    [InlineData("ZAR")]
    public void A_Two_Decimal_Currency_Is_Accepted(string currency)
    {
        var money = Money.Create(19.99m, currency);

        money.Amount.Should().Be(19.99m);
        money.Currency.Should().Be(currency);
    }

    [Theory]
    [InlineData("JPY")]
    [InlineData("KRW")]
    [InlineData("VND")]
    [InlineData("KWD")]
    [InlineData("BHD")]
    [InlineData("TND")]
    public void A_Currency_Whose_Minor_Unit_Is_Not_Two_Digits_Is_Refused(string currency)
    {
        var act = () => Money.Create(1.234m, currency);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("MONEY_UNSUPPORTED_CURRENCY_DECIMALS",
                "silently rounding 1.234 KWD to 1.23 KWD loses money; refusing is the honest " +
                "interim position until a per-currency exponent exists");
    }

    [Fact]
    public void The_Refusal_Is_Case_Insensitive()
    {
        var act = () => Money.Create(1000m, "jpy");

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("MONEY_UNSUPPORTED_CURRENCY_DECIMALS",
                "the code is normalised before the check, or lowercase input would bypass it");
    }

    [Fact]
    public void Amounts_Round_To_Two_Places_Using_Bankers_Rounding()
    {
        Money.Create(0.125m).Amount.Should().Be(0.12m);
        Money.Create(0.135m).Amount.Should().Be(0.14m);
    }

    [Fact]
    public void Multiply_Validates_Instead_Of_Bypassing_Create()
    {
        var unit = Money.Create(9.99m);

        unit.Multiply(3).Should().Be(Money.Create(29.97m));

        var act = () => unit.Multiply(-1);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MONEY_QUANTITY_NEGATIVE");
    }

    [Fact]
    public void Multiply_By_Zero_Is_Zero_Not_A_Refusal()
    {
        Money.Create(9.99m).Multiply(0).Should().Be(Money.Zero());
    }

    [Fact]
    public void Adding_Different_Currencies_Is_Refused()
    {
        var act = () => Money.Create(10m, "USD").Add(Money.Create(10m, "EUR"));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MONEY_CURRENCY_MISMATCH");
    }

    [Fact]
    public void Equality_Includes_The_Currency()
    {
        Money.Create(10m, "USD").Should().Be(Money.Create(10m, "USD"));
        Money.Create(10m, "USD").Should().NotBe(Money.Create(10m, "EUR"),
            "value equality across currencies is what made the payment gate's Money comparison " +
            "able to catch a right-amount/wrong-currency capture");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void A_Negative_Amount_Is_Refused(decimal amount)
    {
        var act = () => Money.Create(amount);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MONEY_NEGATIVE");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("US")]
    [InlineData("USDD")]
    public void A_Malformed_Currency_Code_Is_Refused(string currency)
    {
        var act = () => Money.Create(10m, currency);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MONEY_CURRENCY_INVALID");
    }
}
