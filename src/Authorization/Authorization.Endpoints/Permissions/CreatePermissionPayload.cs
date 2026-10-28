using AuthorizationService.Application.Permissions.Responses;

namespace AuthorizationService.Endpoints.Permissions;

public sealed record CreatePermissionPayload(PermissionDto Permission);
