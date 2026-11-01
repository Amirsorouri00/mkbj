using System.Security.Claims;

using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Customer.QueryApplication.Customers;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using EwaysVNext.Queries.Application.Paginations;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Admin;

/// <summary>
/// Named with the <c>Admin</c> prefix deliberately: an architecture test uses that to tell an
/// admin-facing input, where a partner id is a legitimate filter, from a partner-facing one,
/// where it must never be bindable.
/// </summary>
public sealed record AdminCustomerListInput(
    string? Kind, string? Status, long? PartnerId, string? Search, int? Page, int? PageSize);

[ExtendObjectType(typeof(Query))]
public sealed class CustomerAdminGraphQLQueries
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CustomersManage)]
    public Task<PaginatedQuery<CustomerAdminListItem>> AdminCustomersAsync(
        AdminCustomerListInput? filter,
        [Service] ICustomerAdminQueries queries,
        CancellationToken ct) =>
        queries.ListAsync(
            new AdminCustomerListFilter(
                filter?.Kind, filter?.Status, filter?.PartnerId, filter?.Search,
                filter?.Page ?? 1, filter?.PageSize ?? 20),
            ct);

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CustomersManage)]
    public async Task<CustomerAdminDetail> AdminCustomerAsync(
        long id, [Service] ICustomerAdminQueries queries, CancellationToken ct) =>
        ResultGraphQLExtensions.ValueOrThrow(await queries.GetAsync(id, ct));
}

[ExtendObjectType(typeof(Mutation))]
public sealed class CustomerAdminGraphQLMutations
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CustomersManage)]
    public Task<bool> SuspendCustomerAsync(
        long id, ClaimsPrincipal user,
        [Service] ICustomerAdminService service, CancellationToken ct) =>
        Act(service.SuspendAsync(id, Actor(user), ct));

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CustomersManage)]
    public Task<bool> ReactivateCustomerAsync(
        long id, ClaimsPrincipal user,
        [Service] ICustomerAdminService service, CancellationToken ct) =>
        Act(service.ReactivateAsync(id, Actor(user), ct));

    private static AdminActor Actor(ClaimsPrincipal user) =>
        AdminActor.TryFrom(user, out var actor)
            ? actor!
            : throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("The token passed the admin policy but carries no usable subject.")
                .SetCode("UNAUTHORIZED")
                .Build());

    private static async Task<bool> Act(Task<Result> action)
    {
        (await action).ThrowIfFailure();
        return true;
    }
}
