using AuthorizationService.Application.Roles;
using AuthorizationService.Application.Roles.Responses;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

namespace AuthorizationService.Endpoints.Roles;

[ExtendObjectType(typeof(Query))]
// Who may do what is not public information, and it maps the admin surface for anyone
// probing it.
public sealed class RoleQueries
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    public async Task<IReadOnlyList<RoleDto>> Roles(
        [Service] IRoleService service,
        CancellationToken cancellationToken)
        => (await service.ListAsync(cancellationToken)).ValueOrThrow();
}
