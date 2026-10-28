using AuthorizationService.Application.Roles.Responses;

namespace AuthorizationService.Endpoints.Roles;

public sealed record CreateRolePayload(RoleDto Role);
