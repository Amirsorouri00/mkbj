using System.Text;

namespace EwaysVNext.Domain.ValueObjects;

internal static class MoneyMessages
{
    public const string NegativeAmount = "Money amount cannot be negative.";
    public const string InvalidCurrency = "Currency must be a 3-letter ISO code.";
    public const string UnsupportedCurrencyDecimalsFormat =
        "'{0}' does not use two decimal places, and this template rounds every amount " +
        "to two. Supporting it requires a per-currency minor-unit exponent and matching " +
        "storage precision. Refusing rather than silently mis-rounding the amount.";
    public const string NegativeQuantity = "Quantity cannot be negative.";
    public const string CurrencyMismatchFormat = "Cannot combine {0} with {1}.";
    public static readonly CompositeFormat UnsupportedCurrencyDecimalsComposite = CompositeFormat.Parse(UnsupportedCurrencyDecimalsFormat);
    public static readonly CompositeFormat CurrencyMismatchComposite = CompositeFormat.Parse(CurrencyMismatchFormat);
}
