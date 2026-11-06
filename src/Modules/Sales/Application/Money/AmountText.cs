using System.Globalization;
using System.Text.RegularExpressions;

namespace EwaysVNext.Modules.Sales.Application.Money;

/// <summary>
/// The one amount format Sales accepts and emits: exactly two decimal digits, invariantly.
/// </summary>
/// <remarks>
/// <para>
/// A partner sends the amount a basket must total, and the composition engine is built to hit it
/// to the cent. "500" could mean five hundred or five; "500,00" is five hundred in half of Europe
/// and five in the other half. Both are refused rather than guessed, because the cost of guessing
/// is an invoice for the wrong amount.
/// </para>
/// <para>
/// This duplicates <c>MoneyText</c> in <c>Presentation.Endpoint</c> on purpose. The hexagon may
/// not reference the presentation layer, and the alternative — a shared package for one regex —
/// would couple the two to each other's release cycle. The duplication is three lines; the
/// coupling would not be.
/// </para>
/// </remarks>
public static partial class AmountText
{
    private const int MinorUnitsPerUnit = 100;

    /// <summary>
    /// Parses an amount into minor units, or reports that it is not one.
    /// </summary>
    /// <remarks>
    /// Never throws: the input is a field from an HTTP body, and a malformed one is an ordinary
    /// 400. Overflow is a <c>false</c> too — a wrapped amount would compose a basket against a
    /// negative target, which is the worst available outcome.
    /// </remarks>
    public static bool TryParseMinor(string? text, out long minor)
    {
        minor = 0;

        if (text is null || !Pattern().IsMatch(text)) return false;

        // The pattern has already guaranteed digits, a dot and exactly two more digits, so
        // removing the dot leaves a plain integer in minor units — no rounding, no decimal
        // arithmetic, nothing a floating point type could touch.
        return long.TryParse(
            text.Replace(".", string.Empty, StringComparison.Ordinal),
            NumberStyles.None, CultureInfo.InvariantCulture, out minor);
    }

    /// <summary>Minor units back to the two-decimal string a client sees.</summary>
    public static string Format(long minor) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{minor / MinorUnitsPerUnit}.{Math.Abs(minor % MinorUnitsPerUnit):D2}");

    /// <summary>
    /// No sign, no exponent, no leading dot, no spaces. Anything else is a client bug, and
    /// accepting it would mean deciding on the client's behalf what it meant.
    /// </summary>
    [GeneratedRegex(@"^\d+\.\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
