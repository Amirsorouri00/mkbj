using AuthorizationService.Application.Scopes.Responses;

namespace AuthorizationService.Endpoints.Scopes;

public sealed record UpdateScopePayload(ScopeDto Scope);
