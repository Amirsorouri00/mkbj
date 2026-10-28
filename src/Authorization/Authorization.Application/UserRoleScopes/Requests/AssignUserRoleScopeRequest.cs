namespace AuthorizationService.Application.UserRoleScopes.Requests;

public record AssignUserRoleScopeRequest(Guid UserId, Guid RoleId, Guid ScopeId);
