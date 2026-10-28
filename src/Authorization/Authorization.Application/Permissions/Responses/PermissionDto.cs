namespace AuthorizationService.Application.Permissions.Responses;

public record PermissionDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<PermissionRuleDto> Rules);
