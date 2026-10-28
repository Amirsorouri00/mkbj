using AuthorizationService.Application.UserRoleScopes.Responses;

namespace AuthorizationService.Endpoints.UserRoleScopes;

public sealed record BootstrapAdminPayload(UserRoleScopeDto Assignment);
