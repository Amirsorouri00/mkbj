namespace AuthorizationService.Application.UserRoleScopes.Responses;

public record UserRoleScopeDto(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    string RoleCode,
    Guid ScopeId,
    string ScopeCode,
    DateTime CreatedAtUtc);
