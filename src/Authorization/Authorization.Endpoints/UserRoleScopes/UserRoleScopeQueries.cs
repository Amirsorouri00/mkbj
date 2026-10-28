using AuthorizationService.Application.UserRoleScopes;
using AuthorizationService.Application.UserRoleScopes.Responses;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

namespace AuthorizationService.Endpoints.UserRoleScopes;

[ExtendObjectType(typeof(Query))]
public sealed class UserRoleScopeQueries
{
    public async Task<IReadOnlyList<UserRoleScopeDto>> UserRoleScopesForUser(
        Guid userId,
        [Service] IUserRoleScopeService service,
        CancellationToken cancellationToken)
        => (await service.ListForUserAsync(userId, cancellationToken)).ValueOrThrow();
}
