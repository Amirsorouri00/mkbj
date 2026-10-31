using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer;

namespace EwaysVNext.Modules.Customer.Application.Customers.Ports.Outbound;

public interface ICustomerRepository
{
    Task<CustomerAggregate?> GetAsync(long id, CancellationToken ct = default);

    Task<CustomerAggregate?> FindPartnerLinkedAsync(
        long partnerId, string externalCustomerId, CancellationToken ct = default);

    Task<CustomerAggregate?> FindWebsiteAsync(Guid identityUserId, CancellationToken ct = default);

    void Add(CustomerAggregate customer);
}
