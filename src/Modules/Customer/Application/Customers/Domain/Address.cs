using System.Text.RegularExpressions;

using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Customer.Application.Customers.Domain;

/// <summary>
/// A billing address. Deliberately unopinionated about everything except the country code:
/// address formats differ enough between countries that validating structure would reject real
/// addresses, while the country is what decides tax treatment and shipping, so it must be exact.
/// </summary>
public sealed partial class Address : ValueObject
{
    private Address(string line1, string? line2, string city, string postalCode, string countryIso2)
    {
        Line1 = line1;
        Line2 = line2;
        City = city;
        PostalCode = postalCode;
        CountryIso2 = countryIso2;
    }

    public string Line1 { get; }

    public string? Line2 { get; }

    public string City { get; }

    public string PostalCode { get; }

    /// <summary>ISO 3166-1 alpha-2, uppercased.</summary>
    public string CountryIso2 { get; }

    public static Address Create(string line1, string? line2, string city, string postalCode, string countryIso2)
    {
        var country = (countryIso2 ?? string.Empty).Trim().ToUpperInvariant();

        if (!CountryCode().IsMatch(country))
        {
            throw new DomainException("VALIDATION_FAILED",
                $"'{countryIso2}' is not an ISO 3166-1 alpha-2 country code. Use two letters, for example 'SG'.");
        }

        return new Address(
            Require(line1, nameof(line1), 200),
            string.IsNullOrWhiteSpace(line2) ? null : Require(line2, nameof(line2), 200),
            Require(city, nameof(city), 100),
            Require(postalCode, nameof(postalCode), 20),
            country);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Line1;
        yield return Line2;
        yield return City;
        yield return PostalCode;
        yield return CountryIso2;
    }

    private static string Require(string? value, string name, int maxLength)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
            throw new DomainException("VALIDATION_FAILED", $"{name} is required.");

        if (trimmed.Length > maxLength)
            throw new DomainException("VALIDATION_FAILED", $"{name} cannot exceed {maxLength} characters.");

        return trimmed;
    }

    [GeneratedRegex(@"^[A-Z]{2}$")]
    private static partial Regex CountryCode();
}
