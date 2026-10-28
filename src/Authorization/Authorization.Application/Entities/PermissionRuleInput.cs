namespace AuthorizationService.Application.Entities;

public record PermissionRuleInput(Guid? RoleId, IEnumerable<Guid> ScopeIds);
