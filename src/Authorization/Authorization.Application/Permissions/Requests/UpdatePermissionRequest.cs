namespace AuthorizationService.Application.Permissions.Requests;

public record UpdatePermissionRequest(
    string Name,
    string? Description,
    IReadOnlyList<PermissionRuleRequest> Rules);
