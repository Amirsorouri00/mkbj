using System.Globalization;
using System.Text.RegularExpressions;

namespace EwaysVNext.Modules.Sourcing.Application.Normalization;

/// <summary>
/// Turns whatever a source wrote for a price into exact minor units.
/// </summary>
/// <remarks>
/// <para>
/// Money never becomes a <c>double</c> here, not even briefly. Sources write
/// <c>"1.234,56 €"</c>, <c>"$1,234.56"</c>, <c>"1 234,56"</c> and <c>"19.99"</c>, and the whole
/// difficulty is deciding which separator is the decimal one — a question about notation, not
/// about arithmetic.
/// </para>
/// <para>
/// The rule is: the <b>last</b> separator wins if what follows it is one or two digits. Three
/// digits after the last separator means it is a thousands separator and the price is whole.
/// That gets <c>1.234</c> right as one thousand two hundred and thirty-four, which a naive
/// decimal parse turns into one pound twenty-three.
/// </para>
/// </remarks>
public static partial class PriceParser
{
    /// <summary>Everything that is not a digit or a separator.</summary>
    [GeneratedRegex(@"[^\d.,\s']")]
    private static partial Regex Noise { get; }

    /// <summary>
    /// The amount in minor units, or null when the text is not a price.
    /// </summary>
    /// <remarks>
    /// Null rather than zero: a price that could not be read is a validation error, and zero is
    /// a price somebody might charge.
    /// </remarks>
    public static long? ToMinor(string? text, int decimals = 2)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cleaned = Noise.Replace(text, string.Empty).Trim();
        if (cleaned.Length == 0) return null;

        // Space and apostrophe are only ever grouping separators.
        cleaned = cleaned.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal);

        var lastDot = cleaned.LastIndexOf('.');
        var lastComma = cleaned.LastIndexOf(',');
        var lastSeparator = Math.Max(lastDot, lastComma);

        string whole;
        string fraction;

        if (lastSeparator < 0)
        {
            whole = cleaned;
            fraction = string.Empty;
        }
        else
        {
            var tail = cleaned[(lastSeparator + 1)..];

            if (tail.Length is 1 or 2 && tail.All(char.IsAsciiDigit))
            {
                whole = cleaned[..lastSeparator];
                fraction = tail;
            }
            else
            {
                // Three or more digits after the last separator: it was grouping all along.
                whole = cleaned;
                fraction = string.Empty;
            }
        }

        whole = new string([.. whole.Where(char.IsAsciiDigit)]);
        if (whole.Length == 0 && fraction.Length == 0) return null;
        if (whole.Length == 0) whole = "0";

        if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out var major))
            return null;

        // Pad or truncate to the currency's own scale rather than assuming two.
        fraction = fraction.PadRight(decimals, '0')[..decimals];

        var minorPart = fraction.Length == 0
            ? 0
            : long.Parse(fraction, NumberStyles.None, CultureInfo.InvariantCulture);

        var scale = (long)Math.Pow(10, decimals);

        return checked((major * scale) + minorPart);
    }
}
