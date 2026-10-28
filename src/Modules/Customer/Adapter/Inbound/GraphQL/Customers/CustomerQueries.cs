using EwaysVNext.Domain.Common;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

[ExtendObjectType(typeof(Query))]
public sealed class CustomerQueries
{
    [UseOffsetPaging(IncludeTotalCount = true)]
    public IQueryable<CustomerAdminListItem> Customers(
        CustomerAdminListFilter? filter,
        [Service] ICustomerRepository repository,
        [Service] IClock clock)
    {
        filter ??= new CustomerAdminListFilter();

        if (filter.FromDate is null && filter.ToDate is null)
        {
            var today = clock.UtcNow.UtcDateTime.Date;
            filter = filter with { FromDate = today, ToDate = today };
        }

        return repository.QueryForAdminList(filter);
    }
}
