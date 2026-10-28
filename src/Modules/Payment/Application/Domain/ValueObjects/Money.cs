using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

public sealed class Money : ValueObject
{
    public const string RialCurrency = "IRR";
    private const int SupportedDecimals = 2;

    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainException("MONEY_CURRENCY_INVALID", "Currency must be IRR (Iranian rial).");
        if (!string.Equals(currency, RialCurrency, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("MONEY_CURRENCY_UNSUPPORTED", "Only IRR (Iranian rial) is supported.");

        Amount = amount;
        Currency = RialCurrency;
    }

    public static Money Create(decimal amount, string currency = RialCurrency)
    {
        if (amount < 0) throw new DomainException("MONEY_NEGATIVE", "Money amount cannot be negative.");
        return new Money(Math.Round(amount, SupportedDecimals, MidpointRounding.ToEven), currency);
    }

    public static Money Zero(string currency = RialCurrency) => Create(0m, currency);

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Create(Amount + other.Amount, Currency);
    }

    public Money Multiply(int quantity)
    {
        if (quantity < 0)
            throw new DomainException("MONEY_QUANTITY_NEGATIVE", "Quantity cannot be negative.");

        return Create(Amount * quantity, Currency);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:N2} {Currency}";
}
