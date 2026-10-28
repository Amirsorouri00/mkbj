namespace EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

internal static class NationalCodeMessages
{
    public const string Required = "National code is required.";
    public const string MustBeExactlyTenDigits = "National code must be exactly 10 digits.";
    public const string ChecksumInvalid = "National code checksum is invalid.";
}
