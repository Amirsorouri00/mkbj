using AwesomeAssertions;
using Xunit;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

namespace EwaysVNext.PaymentTests;

public class MoneyTests
{
    [Theory]
    [InlineData("IRR")]
    [InlineData("irr")]
    [InlineData("Irr")]
    public void Rial_Is_Accepted_And_Normalized(string currency)
    {
        var money = Money.Create(19.99m, currency);

        money.Amount.Should().Be(19.99m);
        money.Currency.Should().Be("IRR");
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("CHF")]
    [InlineData("ZAR")]
    [InlineData("IRT")]
    [InlineData("irt")]
    [InlineData("usd")]
    [InlineData("JPY")]
    [InlineData("KRW")]
    [InlineData("VND")]
    [InlineData("KWD")]
    [InlineData("BHD")]
    [InlineData("TND")]
    public void Any_Currency_Other_Than_Rial_Is_Refused(string currency)
    {
        var act = () => Money.Create(1.234m, currency);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("MONEY_CURRENCY_UNSUPPORTED");

        var zero = () => Money.Zero(currency);
        zero.Should().Throw<DomainException>().Which.Code.Should().Be("MONEY_CURRENCY_UNSUPPORTED");
    }

    [Fact]
    public void Default_And_Zero_Are_Rial()
    {
        Money.Create(1000m).Currency.Should().Be("IRR");
        Money.Zero().Currency.Should().Be("IRR");
        Money.Zero().Amount.Should().Be(0);
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
    public void Addition_And_Multiplication_Preserve_Rial()
    {
        var result = Money.Create(10m).Add(Money.Create(20m, "irr")).Multiply(2);
        result.Amount.Should().Be(60m);
        result.Currency.Should().Be("IRR");
    }

    [Fact]
    public void Equality_Uses_The_Normalized_Rial_Amount()
    {
        Money.Create(10m).Should().Be(Money.Create(10m, "irr"));
        Money.Create(10m).Should().NotBe(Money.Create(11m));
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
    [InlineData(null)]
    public void A_Malformed_Currency_Code_Is_Refused(string? currency)
    {
        var act = () => Money.Create(10m, currency!);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MONEY_CURRENCY_INVALID");
    }
}
