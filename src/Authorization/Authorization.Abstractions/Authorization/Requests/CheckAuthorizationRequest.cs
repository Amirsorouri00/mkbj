namespace AuthorizationService.Abstractions.Authorization.Requests;

public record CheckAuthorizationRequest(Guid UserId, string PermissionCode, Guid? ScopeId);
