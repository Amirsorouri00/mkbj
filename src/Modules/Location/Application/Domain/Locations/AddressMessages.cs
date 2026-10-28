namespace EwaysVNext.Modules.Location.Application.Domain.Locations;

internal static class AddressMessages
{
    public const string CityIdMustBeGreaterThanZero = "CityId must be greater than zero.";
    public const string AddressTooLong = "Address cannot exceed 1000 characters.";
    public const string RecipientNameTooLong = "RecipientName cannot exceed 200 characters.";
    public const string RecipientMobileTooLong = "RecipientMobile cannot exceed 20 characters.";
    public const string PrimaryAddressCannotBeModified = "The primary address cannot be edited or deleted.";
}
