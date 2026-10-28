namespace AuthorizationService.Application.Permissions.Requests;

public record CreatePermissionRequest(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<PermissionRuleRequest> Rules);
