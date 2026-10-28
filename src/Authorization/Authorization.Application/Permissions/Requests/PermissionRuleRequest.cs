namespace AuthorizationService.Application.Permissions.Requests;

public record PermissionRuleRequest(Guid? RoleId, IReadOnlyList<Guid>? ScopeIds);
