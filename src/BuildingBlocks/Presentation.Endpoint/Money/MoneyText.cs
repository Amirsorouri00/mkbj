using System.Globalization;
using System.Text.RegularExpressions;

namespace EwaysVNext.Presentation.Endpoint.Money;

/// <summary>
/// The one wire format for money: exactly <c>digits.dd</c>, parsed to and formatted from minor
/// units.
/// <para>
/// Not a lenient decimal parse, deliberately. "25" and "25.0" are almost certainly what the
/// caller meant, but accepting them means the API quietly decides what a half-written amount
/// stands for — and the one place that matters is money. Refusing makes the caller say it. A
/// leading sign is not in the pattern either, so a negative amount is a format error rather than
/// something a domain rule has to catch later.
/// </para>
/// </summary>
public static partial class MoneyText
{
    /// <summary>
    /// Every currency the MVP supports has two minor units. This mirrors
    /// <c>EwaysVNext.Persistence.MinorUnits</c>, which presentation must not reference — the
    /// dependency would run the wrong way.
    /// </summary>
    private const int Factor = 100;

    public static bool TryParseMinor(string? text, out long minor)
    {
        minor = 0;

        if (text is null || !Pattern().IsMatch(text))
            return false;

        try
        {
            // Checked, and the decimal parse itself can overflow: a caller can type more digits
            // than either type holds, and a silently wrapped amount is the worst outcome here.
            // The pattern has already guaranteed exactly two decimals, so nothing is rounded.
            minor = checked((long)(decimal.Parse(text, CultureInfo.InvariantCulture) * Factor));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static string Format(long minor) =>
        (minor / (decimal)Factor).ToString("0.00", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\d+\.\d{2}$")]
    private static partial Regex Pattern();
}
