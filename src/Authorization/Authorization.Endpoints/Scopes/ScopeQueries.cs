using AuthorizationService.Application.Scopes;
using AuthorizationService.Application.Scopes.Responses;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

namespace AuthorizationService.Endpoints.Scopes;

[ExtendObjectType(typeof(Query))]
public sealed class ScopeQueries
{
    public async Task<IReadOnlyList<ScopeDto>> Scopes(
        [Service] IScopeService service,
        CancellationToken cancellationToken)
        => (await service.ListAsync(cancellationToken)).ValueOrThrow();
}
