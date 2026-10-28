using EwaysVNext.Domain.Common;
using System.Text.RegularExpressions;

namespace EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

public sealed record CompanyNationalId
{
    private static readonly Regex ElevenDigits = new(@"^[0-9]{11}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Value { get; }

    private CompanyNationalId(string value) => Value = value;

    public static CompanyNationalId Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ElevenDigits.IsMatch(value))
            throw new DomainException(
                "LEGALINFO_NATIONALID_INVALID",
                "National ID must be exactly 11 digits, numbers only");

        return new CompanyNationalId(value);
    }

    public override string ToString() => Value;
}
