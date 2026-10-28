using AuthorizationService.Application.Permissions;
using AuthorizationService.Application.Permissions.Requests;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using HotChocolate.Authorization;

namespace AuthorizationService.Endpoints.Permissions;

[ExtendObjectType(typeof(Mutation))]
public sealed class PermissionMutations
{
    [Authorize]
    public async Task<CreatePermissionPayload> CreatePermission(
        CreatePermissionInput input,
        [Service] IPermissionService service,
        CancellationToken cancellationToken)
    {
        var request = new CreatePermissionRequest(input.Code, input.Name, input.Description, ToRuleRequests(input.Rules));
        var permission = await service.CreateAsync(request, cancellationToken);
        return permission.ValueOrThrow(new CreatePermissionPayload(permission.Value!));
    }

    [Authorize]
    public async Task<UpdatePermissionPayload> UpdatePermission(
        UpdatePermissionInput input,
        [Service] IPermissionService service,
        CancellationToken cancellationToken)
    {
        var request = new UpdatePermissionRequest(input.Name, input.Description, ToRuleRequests(input.Rules));
        var permission = await service.UpdateAsync(input.Id, request, cancellationToken);
        return permission.ValueOrThrow( new UpdatePermissionPayload(permission.Value!));
    }

    [Authorize]
    public async Task<DeletePermissionPayload> DeletePermission(
        DeletePermissionInput input,
        [Service] IPermissionService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(input.Id, cancellationToken);

        return result.ValueOrThrow(new DeletePermissionPayload(true));
    }

    private static List<PermissionRuleRequest> ToRuleRequests(IReadOnlyList<PermissionRuleInput> rules) =>
        [.. rules.Select(rule => new PermissionRuleRequest(rule.RoleId, rule.ScopeIds))];
}
