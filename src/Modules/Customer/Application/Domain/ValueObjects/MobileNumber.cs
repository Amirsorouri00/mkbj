using EwaysVNext.Domain.Common;
using System.Text.RegularExpressions;

namespace EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

public sealed record MobileNumber
{
    private static readonly Regex IranMobileRegex =
        new(@"^\+989\d{9}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Value { get; }

    private MobileNumber(string value)
    {
        Value = value;
    }

    public static MobileNumber Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("MOBILENUMBER_REQUIRED", MobileNumberMessages.Required);

        var normalized = Normalize(value);

        if (!IranMobileRegex.IsMatch(normalized))
            throw new DomainException("MOBILENUMBER_CHECKSUM_IS_INVALID", MobileNumberMessages.ChecksumInvalid);

        return new MobileNumber(normalized);
    }

    private static string Normalize(string value)
    {
        var input = value.Trim()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);

        // Persian/Arabic digits -> Latin digits.
        input = input
            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2')
            .Replace('۳', '3').Replace('۴', '4').Replace('۵', '5')
            .Replace('۶', '6').Replace('۷', '7').Replace('۸', '8')
            .Replace('۹', '9')
            .Replace('٠', '0').Replace('١', '1').Replace('٢', '2')
            .Replace('٣', '3').Replace('٤', '4').Replace('٥', '5')
            .Replace('٦', '6').Replace('٧', '7').Replace('٨', '8')
            .Replace('٩', '9');

        if (input.StartsWith("00989", StringComparison.Ordinal))
            return "+" + input[2..];

        if (input.StartsWith("989", StringComparison.Ordinal))
            return "+" + input;

        if (input.StartsWith("09", StringComparison.Ordinal))
            return "+98" + input[1..];

        if (input.StartsWith("+989", StringComparison.Ordinal))
            return input;

        return input;
    }

    public override string ToString() => Value;
}
