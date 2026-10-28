namespace AuthorizationService.Endpoints.Permissions;

public sealed record CreatePermissionInput(string Code, string Name, string? Description, IReadOnlyList<PermissionRuleInput> Rules);
