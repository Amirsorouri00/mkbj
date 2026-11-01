using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;

namespace EwaysVNext.Modules.Customer.QueryApplication.Customers;

/// <summary>
/// <paramref name="Search"/> matches across email, phone, name, company and the partner's own
/// reference, because an admin searching for a customer has whichever of those the person on the
/// phone just gave them.
/// </summary>
public sealed record AdminCustomerListFilter(
    string? Kind,
    string? Status,
    long? PartnerId,
    string? Search,
    int Page,
    int PageSize);

public sealed record CustomerAdminListItem(
    long Id,
    string Kind,
    long? PartnerId,
    string? ExternalCustomerId,
    string? Email,
    string? Phone,
    string? FullName,
    string? CompanyName,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record ContactChangeView(
    string Field,
    string? OldValue,
    string? NewValue,
    DateTimeOffset ChangedAt,
    long? SourcePartnerId,
    string CorrelationId);

public sealed record CustomerAdminDetail(
    CustomerAdminListItem Customer,
    bool EmailVerified,
    AddressSnapshot? BillingAddress,
    string? RegistrationNumber,
    Guid? IdentityUserId,
    IReadOnlyList<ContactChangeView> ContactHistory);
