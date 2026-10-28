using AuthorizationService.Application.Scopes;
using AuthorizationService.Application.Scopes.Responses;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

namespace AuthorizationService.Endpoints.Scopes;

[ExtendObjectType(typeof(Query))]
// Who may do what is not public information, and it maps the admin surface for anyone
// probing it.
[Authorize(Policy = AuthPolicies.AdminStaff)]
public sealed class ScopeQueries
{
    public async Task<IReadOnlyList<ScopeDto>> Scopes(
        [Service] IScopeService service,
        CancellationToken cancellationToken)
        => (await service.ListAsync(cancellationToken)).ValueOrThrow();
}
