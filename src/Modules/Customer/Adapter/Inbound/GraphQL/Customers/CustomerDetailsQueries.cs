using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

[ExtendObjectType(typeof(Query))]
public sealed class CustomerDetailsQueries
{
    public Task<CustomerAdminDetails?> CustomerById(
        [Service] ICustomerRepository repository,
        long customerId,
        CancellationToken cancellationToken) =>
        repository.GetAdminDetailsAsync(customerId, cancellationToken);
}
