namespace AuthorizationService.Endpoints.Permissions;

public sealed record UpdatePermissionInput(Guid Id, string Name, string? Description, IReadOnlyList<PermissionRuleInput> Rules);
