using System.Security.Claims;
using AuthorizationService.Application.UserRoleScopes;
using AuthorizationService.Application.UserRoleScopes.Requests;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using HotChocolate.Authorization;

namespace AuthorizationService.Endpoints.UserRoleScopes;

[ExtendObjectType(typeof(Mutation))]
public sealed class UserRoleScopeMutations
{
    [Authorize]
    public async Task<BootstrapAdminPayload> BootstrapAdmin(
        BootstrapAdminInput input,
        ClaimsPrincipal caller,
        [Service] IUserRoleScopeService service,
        CancellationToken cancellationToken)
    {
        var userIdClaim = caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? caller.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage(UserRoleScopeMutationsMessages.InvalidUserIdentifier)
                .SetCode(ErrorCodes.Authentication.NotAuthenticated)
                .Build());
        }

        var assignment = (await service.BootstrapAdminAsync(userId, input.ScopeId, cancellationToken)).ValueOrThrow();

        return new BootstrapAdminPayload(assignment);
    }

    [Authorize]
    public async Task<AssignUserRoleScopePayload> AssignUserRoleScope(
        AssignUserRoleScopeInput input,
        [Service] IUserRoleScopeService service,
        CancellationToken cancellationToken)
    {
        var request = new AssignUserRoleScopeRequest(input.UserId, input.RoleId, input.ScopeId);
        var assignment = (await service.AssignAsync(request, cancellationToken)).ValueOrThrow();

        return new AssignUserRoleScopePayload(assignment);
    }

    [Authorize]
    public async Task<RemoveUserRoleScopePayload> RemoveUserRoleScope(
        RemoveUserRoleScopeInput input,
        [Service] IUserRoleScopeService service,
        CancellationToken cancellationToken)
    {
        var result = await service.RemoveAsync(input.Id, cancellationToken);

        return result.ValueOrThrow(new RemoveUserRoleScopePayload(true));
    }
}
