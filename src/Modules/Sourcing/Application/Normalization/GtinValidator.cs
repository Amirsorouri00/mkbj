namespace EwaysVNext.Modules.Sourcing.Application.Normalization;

/// <summary>
/// Whether a barcode is a real one.
/// </summary>
/// <remarks>
/// <para>
/// GTINs carry a check digit, so a mistyped one is detectable — and worth detecting, because a
/// GTIN is the strongest thing the matcher has. A wrong barcode that passes silently attaches a
/// listing to somebody else's product, which is the expensive mistake this whole module is
/// arranged to avoid.
/// </para>
/// <para>
/// A short GTIN is left-padded to fourteen digits rather than rejected: GTIN-8, -12 and -13 are
/// all legal, and padding is how the standard says to compare them.
/// </para>
/// </remarks>
public static class GtinValidator
{
    /// <summary>The digits-only form, if the text is a valid GTIN of any length.</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var digits = new string([.. text.Where(char.IsAsciiDigit)]);

        if (digits.Length is not (8 or 12 or 13 or 14)) return null;

        return IsCheckDigitValid(digits) ? digits : null;
    }

    public static bool IsCheckDigitValid(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);

        if (digits.Length is not (8 or 12 or 13 or 14)) return false;

        var padded = digits.PadLeft(14, '0');
        var sum = 0;

        // Weights alternate 3 and 1 from the right, excluding the check digit itself.
        for (var index = 0; index < 13; index++)
        {
            var value = padded[index] - '0';
            sum += value * ((index % 2 == 0) ? 3 : 1);
        }

        var expected = (10 - (sum % 10)) % 10;

        return expected == padded[13] - '0';
    }
}
