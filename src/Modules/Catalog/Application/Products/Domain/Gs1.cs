namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

/// <summary>
/// GS1 check-digit arithmetic, shared by every numeric identifier type.
/// </summary>
internal static class Gs1
{
    /// <summary>
    /// The standard GS1 mod-10 check: weight the payload digits 3 and 1 alternately starting from
    /// the digit immediately left of the check digit, and the check digit is what rounds the sum
    /// up to a multiple of ten.
    /// <para>
    /// Weighting from the <em>right</em> rather than the left is what makes one routine serve
    /// GTIN-8 through GTIN-14: the weights line up with the payload regardless of its length.
    /// </para>
    /// </summary>
    public static bool HasValidCheckDigit(string digits)
    {
        if (string.IsNullOrEmpty(digits) || digits.Length < 2)
            return false;

        var sum = 0;
        var weight = 3;

        for (var index = digits.Length - 2; index >= 0; index--)
        {
            var digit = digits[index] - '0';
            if (digit is < 0 or > 9)
                return false;

            sum += digit * weight;
            weight = weight == 3 ? 1 : 3;
        }

        var expected = (10 - (sum % 10)) % 10;
        return digits[^1] - '0' == expected;
    }
}
