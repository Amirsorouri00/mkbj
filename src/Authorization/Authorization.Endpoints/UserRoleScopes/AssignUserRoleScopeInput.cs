namespace AuthorizationService.Endpoints.UserRoleScopes;

public sealed record AssignUserRoleScopeInput(Guid UserId, Guid RoleId, Guid ScopeId);
