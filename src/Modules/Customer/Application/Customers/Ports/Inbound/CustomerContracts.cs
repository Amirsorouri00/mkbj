namespace EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;

/// <summary>
/// Contact details as they arrive from a partner: raw strings, because the partner's wire format
/// is not this module's value objects. Validation happens at this boundary, where a malformed
/// value can still be reported as a refusal rather than thrown.
/// </summary>
public sealed record ContactInput(string? Email, string? Phone, string? FullName);

/// <summary>What a caller needs to attach an order to a customer, and nothing more.</summary>
public sealed record PartnerCustomerRef(long CustomerId, string? Email, string? Phone, string? FullName);

public sealed record AddressSnapshot(
    string Line1, string? Line2, string City, string PostalCode, string CountryIso2);

/// <summary>Everything that appears on an invoice's "bill to" block.</summary>
public sealed record InvoiceParty(
    long CustomerId,
    string? FullName,
    string? CompanyName,
    string? Email,
    string? Phone,
    AddressSnapshot? BillingAddress,
    string? RegistrationNumber);

public sealed record WebsiteCustomerRef(long CustomerId, bool EmailVerified, bool IsActive);
