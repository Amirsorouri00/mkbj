namespace EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

internal static class PhoneNumberMessages
{
    public const string Required = "phone number is required.";
    public const string Invalid = "phone number is invalid. Expected a mobile (09xxxxxxxxx) or a landline with its area code (0xxxxxxxxxx).";
}
