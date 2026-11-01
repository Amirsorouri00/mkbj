using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;

/// <summary>
/// Email is absent on purpose. Keycloak owns the address for a website account, and letting a
/// customer change it here would leave the two disagreeing — with the identity provider winning
/// at the next sign-in, silently undoing the change.
/// </summary>
public sealed record UpdateProfileCommand(
    string? FullName,
    string? CompanyName,
    string? Phone,
    AddressSnapshot? BillingAddress,
    string? RegistrationNumber);

public interface ICustomerProfileService
{
    Task<Result> UpdateProfileAsync(
        long customerId, UpdateProfileCommand command, string correlationId, CancellationToken ct = default);
}
