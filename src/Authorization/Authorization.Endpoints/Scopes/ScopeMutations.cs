using AuthorizationService.Application.Scopes;
using AuthorizationService.Application.Scopes.Requests;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.Security;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using HotChocolate.Authorization;

namespace AuthorizationService.Endpoints.Scopes;

[ExtendObjectType(typeof(Mutation))]
// Editing the permission matrix is a super-admin act: an ungated mutation here lets a
// caller grant themselves any permission, this one included.
[Authorize(Policy = AuthPolicies.AdminSuper)]
public sealed class ScopeMutations
{
    public async Task<CreateScopePayload> CreateScope(
        CreateScopeInput input,
        [Service] IScopeService service,
        CancellationToken cancellationToken)
    {
        var scope = (await service.CreateAsync(new CreateScopeRequest(input.Code, input.Name), cancellationToken)).ValueOrThrow();

        return new CreateScopePayload(scope);
    }

    public async Task<UpdateScopePayload> UpdateScope(
        UpdateScopeInput input,
        [Service] IScopeService service,
        CancellationToken cancellationToken)
    {
        var scope = (await service.UpdateAsync(input.Id, new UpdateScopeRequest(input.Name), cancellationToken)).ValueOrThrow();

        return new UpdateScopePayload(scope);
    }

    public async Task<DeleteScopePayload> DeleteScope(
        DeleteScopeInput input,
        [Service] IScopeService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(input.Id, cancellationToken);

        return result.ValueOrThrow(new DeleteScopePayload(true));
    }
}
