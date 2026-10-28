using EwaysVNext.Domain.Common;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

public sealed class NationalCode : IEquatable<NationalCode>
{
    private readonly string _value;

    private NationalCode(string value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the string representation of the national code
    /// </summary>
    public string Value => _value;

    /// <summary>
    /// Creates a national code instance with validation
    /// </summary>
    public static NationalCode Create(string nationalCode)
    {
        if (string.IsNullOrWhiteSpace(nationalCode))
            throw new DomainException("NATIONALCODE_REQUIRED", NationalCodeMessages.Required);

        // Normalize Persian/Arabic digits to English
        var normalized = NormalizePersianArabicDigits(nationalCode);

        // Validate format
        if (!Regex.IsMatch(normalized, @"^[0-9]{10}$"))
            throw new DomainException("NATIONALCODE_MUST_BE_EXACTLY_TEN_DIGITS", NationalCodeMessages.MustBeExactlyTenDigits);

        // Validate checksum
        if (!IsValidNationalCodeChecksum(normalized))
            throw new DomainException("NATIONALCODE_CHECKSUM_IS_INVALID", NationalCodeMessages.ChecksumInvalid);

        return new NationalCode(normalized);
    }

    /// <summary>
    /// Normalizes Persian (۰-۹) and Arabic (٠-٩) digits to English (0-9)
    /// </summary>
    private static string NormalizePersianArabicDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Persian digits
        var result = input.Replace("۰", "0")
                         .Replace("۱", "1")
                         .Replace("۲", "2")
                         .Replace("۳", "3")
                         .Replace("۴", "4")
                         .Replace("۵", "5")
                         .Replace("۶", "6")
                         .Replace("۷", "7")
                         .Replace("۸", "8")
                         .Replace("۹", "9");

        // Arabic digits
        result = result.Replace("٠", "0")
                      .Replace("١", "1")
                      .Replace("٢", "2")
                      .Replace("٣", "3")
                      .Replace("٤", "4")
                      .Replace("٥", "5")
                      .Replace("٦", "6")
                      .Replace("٧", "7")
                      .Replace("٨", "8")
                      .Replace("٩", "9");

        return result;
    }

    /// <summary>
    /// Validates Iranian National Code checksum using Luhn algorithm
    /// </summary>
    private static bool IsValidNationalCodeChecksum(string nationalCode)
    {
        if (nationalCode.Length != 10)
            return false;

        var check = int.Parse(nationalCode[9].ToString(), CultureInfo.InvariantCulture);
        var sum = 0;

        for (var i = 0; i < 9; i++)
        {
            sum += int.Parse(nationalCode[i].ToString(), CultureInfo.InvariantCulture) * (10 - i);
        }

        var remainder = sum % 11;
        return remainder < 2 ? check == remainder : check == 11 - remainder;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as NationalCode);
    }

    public bool Equals(NationalCode? other)
    {
        if (other is null)
            return false;

        return _value == other._value;
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public override string ToString()
    {
        return _value;
    }

    public static bool operator ==(NationalCode? left, NationalCode? right)
    {
        if (left is null && right is null)
            return true;

        if (left is null || right is null)
            return false;

        return left.Equals(right);
    }

    public static bool operator !=(NationalCode? left, NationalCode? right)
    {
        return !(left == right);
    }
}
