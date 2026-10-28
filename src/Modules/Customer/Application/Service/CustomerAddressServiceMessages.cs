namespace EwaysVNext.Modules.Customer.Application.Service;

internal static class CustomerAddressServiceMessages
{
    public const string CustomerNotFound = "Customer was not found.";
    public const string AddressNotFound = "Address was not found.";
    public const string InvalidPostalCode = "Postal code is required and must be exactly 10 digits.";
    public const string InvalidCity = "The selected city does not exist.";
    public const string InvalidDistrict = "The selected district does not belong to the selected city.";
    public const string AddressTextTooLong = "Address cannot exceed 1000 characters.";
    public const string RecipientNameRequired = "Recipient name is required when the customer is not the recipient.";
    public const string RecipientNameTooLong = "Recipient name cannot exceed 200 characters.";
    public const string CustomerProfileIncomplete = "Complete your profile name before using yourself as the recipient.";
}
