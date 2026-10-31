using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;

/// <summary>
/// Turns an authenticated identity into a customer record, creating one on first sight.
/// <para>
/// Lazily, rather than on registration: nothing in Keycloak tells this system a user signed up,
/// and a webhook would be one more thing that can be missed. The first authenticated request is
/// the first moment the record is actually needed.
/// </para>
/// </summary>
public interface IWebsiteCustomerPort
{
    Task<Result<WebsiteCustomerRef>> EnsureFromIdentityAsync(
        Guid identityUserId, string email, bool emailVerified, CancellationToken ct = default);
}
