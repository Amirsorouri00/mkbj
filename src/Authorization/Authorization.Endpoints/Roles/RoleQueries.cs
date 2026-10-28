using AuthorizationService.Application.Roles;
using AuthorizationService.Application.Roles.Responses;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

namespace AuthorizationService.Endpoints.Roles;

[ExtendObjectType(typeof(Query))]
public sealed class RoleQueries
{
    public async Task<IReadOnlyList<RoleDto>> Roles(
        [Service] IRoleService service,
        CancellationToken cancellationToken)
        => (await service.ListAsync(cancellationToken)).ValueOrThrow();
}
