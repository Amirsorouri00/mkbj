using AuthorizationService.Application.Permissions.Responses;

namespace AuthorizationService.Endpoints.Permissions;

public sealed record UpdatePermissionPayload(PermissionDto Permission);
