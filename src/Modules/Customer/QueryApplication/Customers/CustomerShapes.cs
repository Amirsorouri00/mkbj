using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;

namespace EwaysVNext.Modules.Customer.QueryApplication.Customers;

/// <summary>
/// A customer's own view of themselves. Carries no partner fields: a website customer has none,
/// and a shape that could show them would be one more place to leak a partner's data.
/// </summary>
public sealed record CustomerProfileView(
    long Id,
    string? Email,
    bool EmailVerified,
    string? Phone,
    string? FullName,
    string? CompanyName,
    AddressSnapshot? BillingAddress,
    string? RegistrationNumber,
    string Status);
