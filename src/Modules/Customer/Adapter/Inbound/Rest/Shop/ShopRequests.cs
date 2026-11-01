namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Shop;

public sealed record AddressRequest(
    string Line1, string? Line2, string City, string PostalCode, string CountryIso2);

/// <summary>
/// No email field. Keycloak owns the address for a website account, so accepting one here would
/// let a customer set a value the next sign-in silently overwrites.
/// </summary>
public sealed record UpdateProfileRequest(
    string? FullName,
    string? CompanyName,
    string? Phone,
    AddressRequest? BillingAddress,
    string? RegistrationNumber);
