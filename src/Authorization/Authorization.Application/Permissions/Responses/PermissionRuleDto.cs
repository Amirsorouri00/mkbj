namespace AuthorizationService.Application.Permissions.Responses;

public record PermissionRuleDto(
    Guid Id,
    Guid? RoleId,
    string? RoleCode,
    IReadOnlyList<PermissionRuleScopeDto> Scopes);
