namespace AuthorizationService.Endpoints.Permissions;

public sealed record PermissionRuleInput(Guid? RoleId, IReadOnlyList<Guid>? ScopeIds);
