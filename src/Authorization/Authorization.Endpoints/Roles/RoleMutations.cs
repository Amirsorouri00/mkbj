using AuthorizationService.Application.Roles;
using AuthorizationService.Application.Roles.Requests;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using HotChocolate.Authorization;

namespace AuthorizationService.Endpoints.Roles;

[ExtendObjectType(typeof(Mutation))]
public sealed class RoleMutations
{
    [Authorize]
    public async Task<CreateRolePayload> CreateRole(
        CreateRoleInput input,
        [Service] IRoleService service,
        CancellationToken cancellationToken)
    {
        var role = (await service.CreateAsync(new CreateRoleRequest(input.Code, input.Name, input.Rank), cancellationToken)).ValueOrThrow();

        return new CreateRolePayload(role);
    }

    [Authorize]
    public async Task<UpdateRolePayload> UpdateRole(
        UpdateRoleInput input,
        [Service] IRoleService service,
        CancellationToken cancellationToken)
    {
        var role = (await service.UpdateAsync(input.Id, new UpdateRoleRequest(input.Name, input.Rank), cancellationToken)).ValueOrThrow();

        return new UpdateRolePayload(role);
    }

    [Authorize]
    public async Task<DeleteRolePayload> DeleteRole(
        DeleteRoleInput input,
        [Service] IRoleService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(input.Id, cancellationToken);

        return result.ValueOrThrow(new DeleteRolePayload(true));
    }
}
