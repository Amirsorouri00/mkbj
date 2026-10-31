using EwaysVNext.Domain.ValueObjects;

namespace EwaysVNext.Modules.Customer.Application.Customers.Domain;

/// <summary>
/// Contact details a partner is sending. <b>Null means omitted, not cleared.</b> A partner that
/// does not store phone numbers sends null every time, and treating that as "delete their phone
/// number" would wipe details the customer gave the seller directly.
/// </summary>
public sealed record ContactUpdate(Email? Email, Phone? Phone, string? FullName);

/// <summary>Profile fields a website customer is changing. Null means omitted, as above.</summary>
public sealed record ProfileUpdate(
    string? FullName,
    string? CompanyName,
    Phone? Phone,
    Address? BillingAddress,
    string? RegistrationNumber);
