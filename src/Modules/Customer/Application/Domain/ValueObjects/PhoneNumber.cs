using EwaysVNext.Domain.Common;
using System.Text.RegularExpressions;

namespace EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

/// <summary>
/// An Iranian contact number - either a mobile (09xxxxxxxxx) or a landline with its area code
/// (0xxxxxxxxxx). Stored in international form (+98 followed by the 10-digit national number),
/// the same shape <see cref="MobileNumber"/> uses, so the two can be compared directly.
/// </summary>
public sealed record PhoneNumber
{
    private static readonly Regex IranPhoneRegex =
        new(@"^\+98[1-9]\d{9}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Value { get; }

    private PhoneNumber(string value)
    {
        Value = value;
    }

    public static PhoneNumber Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("PHONENUMBER_REQUIRED", PhoneNumberMessages.Required);

        var normalized = Normalize(value);

        if (!IranPhoneRegex.IsMatch(normalized))
            throw new DomainException("PHONENUMBER_INVALID", PhoneNumberMessages.Invalid);

        return new PhoneNumber(normalized);
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

        if (input.StartsWith("0098", StringComparison.Ordinal))
            return "+" + input[2..];

        if (input.StartsWith("+98", StringComparison.Ordinal))
            return input;

        if (input.StartsWith("98", StringComparison.Ordinal) && input.Length == 12)
            return "+" + input;

        if (input.StartsWith('0'))
            return "+98" + input[1..];

        return input;
    }

    public override string ToString() => Value;
}
