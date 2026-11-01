using AuthorizationService.Application.UserRoleScopes;
using AuthorizationService.Application.UserRoleScopes.Responses;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

namespace AuthorizationService.Endpoints.UserRoleScopes;

[ExtendObjectType(typeof(Query))]
// Who may do what is not public information, and it maps the admin surface for anyone
// probing it.
public sealed class UserRoleScopeQueries
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    public async Task<IReadOnlyList<UserRoleScopeDto>> UserRoleScopesForUser(
        Guid userId,
        [Service] IUserRoleScopeService service,
        CancellationToken cancellationToken)
        => (await service.ListForUserAsync(userId, cancellationToken)).ValueOrThrow();
}
