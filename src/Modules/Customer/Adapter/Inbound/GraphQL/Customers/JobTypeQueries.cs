using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

[ExtendObjectType(typeof(Query))]
public sealed class JobTypeQueries
{
    public IQueryable<JobTypeItem> JobTypes([Service] ICustomerRepository repository) =>
        repository.QueryJobTypes();
}
