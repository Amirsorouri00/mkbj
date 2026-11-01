using AuthorizationService.Application.Roles;
using AuthorizationService.Application.Roles.Requests;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.Security;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using HotChocolate.Authorization;

namespace AuthorizationService.Endpoints.Roles;

[ExtendObjectType(typeof(Mutation))]
// Editing the permission matrix is a super-admin act: an ungated mutation here lets a
// caller grant themselves any permission, this one included.
public sealed class RoleMutations
{
    [Authorize(Policy = AuthPolicies.AdminSuper)]
    public async Task<CreateRolePayload> CreateRole(
        CreateRoleInput input,
        [Service] IRoleService service,
        CancellationToken cancellationToken)
    {
        var role = (await service.CreateAsync(new CreateRoleRequest(input.Code, input.Name, input.Rank), cancellationToken)).ValueOrThrow();

        return new CreateRolePayload(role);
    }

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    public async Task<UpdateRolePayload> UpdateRole(
        UpdateRoleInput input,
        [Service] IRoleService service,
        CancellationToken cancellationToken)
    {
        var role = (await service.UpdateAsync(input.Id, new UpdateRoleRequest(input.Name, input.Rank), cancellationToken)).ValueOrThrow();

        return new UpdateRolePayload(role);
    }

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    public async Task<DeleteRolePayload> DeleteRole(
        DeleteRoleInput input,
        [Service] IRoleService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(input.Id, cancellationToken);

        return result.ValueOrThrow(new DeleteRolePayload(true));
    }
}
