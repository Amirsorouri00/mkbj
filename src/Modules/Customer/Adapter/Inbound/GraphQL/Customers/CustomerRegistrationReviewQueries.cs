using EwaysVNext.Domain.Common;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

[ExtendObjectType(typeof(Query))]
public sealed class CustomerRegistrationReviewQueries
{
    public Task<CustomerRegistrationReview?> CustomerRegistrationReview(
        [Service] ICustomerRepository repository,
        SnowflakeId customerId,
        CancellationToken cancellationToken) =>
        repository.GetRegistrationReviewAsync(customerId, cancellationToken);
}
