using AuthorizationService.Application.Roles;
using AuthorizationService.Application.Roles.Responses;
using Mkbj.Presentation.Endpoint.GraphQL;
using Mkbj.Presentation.Endpoint.Security;
using HotChocolate.Authorization;
using Mkbj.Presentation.Endpoint.GraphQL.Extensions;

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
