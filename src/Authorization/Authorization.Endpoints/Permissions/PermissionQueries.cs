using AuthorizationService.Application.Permissions;
using AuthorizationService.Application.Permissions.Responses;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

namespace AuthorizationService.Endpoints.Permissions;

[ExtendObjectType(typeof(Query))]
// Who may do what is not public information, and it maps the admin surface for anyone
// probing it.
[Authorize(Policy = AuthPolicies.AdminStaff)]
public sealed class PermissionQueries
{
    public async Task<IReadOnlyList<PermissionDto>> Permissions(
        [Service] IPermissionService service,
        CancellationToken cancellationToken)
        => (await service.ListAsync(cancellationToken)).ValueOrThrow();

    public async Task<PermissionDto?> Permission(
        Guid id,
        [Service] IPermissionService service,
        CancellationToken cancellationToken)
        => (await service.GetByIdAsync(id, cancellationToken)).ValueOrDefault();

    public async Task<PermissionDto?> PermissionByCode(
        string code,
        [Service] IPermissionService service,
        CancellationToken cancellationToken)
        => (await service.GetByCodeAsync(code, cancellationToken)).ValueOrDefault();
}
