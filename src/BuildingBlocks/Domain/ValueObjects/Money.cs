using System.Globalization;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Domain.ValueObjects;

public sealed class Money : ValueObject
{
    private const int SupportedDecimals = 2;

    private static readonly HashSet<string> NonTwoDecimalCurrencies =
        new(StringComparer.Ordinal)
        {
            "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG",
            "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF",
            "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND",
            "CLF", "UYW",
        };

    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency = "USD")
    {
        if (amount < 0) throw new DomainException("MONEY_NEGATIVE", MoneyMessages.NegativeAmount);
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainException("MONEY_CURRENCY_INVALID", MoneyMessages.InvalidCurrency);

        var code = currency.ToUpperInvariant();
        if (NonTwoDecimalCurrencies.Contains(code))
            throw new DomainException("MONEY_UNSUPPORTED_CURRENCY_DECIMALS",
                string.Format(CultureInfo.InvariantCulture, MoneyMessages.UnsupportedCurrencyDecimalsComposite, code));

        return new Money(Math.Round(amount, SupportedDecimals, MidpointRounding.ToEven), code);
    }

    public static Money Zero(string currency = "USD") => Create(0m, currency);

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);
        return Create(Amount + other.Amount, Currency);
    }

    public Money Multiply(int quantity)
    {
        if (quantity < 0)
            throw new DomainException("MONEY_QUANTITY_NEGATIVE", MoneyMessages.NegativeQuantity);

        return Create(Amount * quantity, Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("MONEY_CURRENCY_MISMATCH", string.Format(CultureInfo.InvariantCulture, MoneyMessages.CurrencyMismatchComposite, Currency, other.Currency));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:N2} {Currency}";
}
