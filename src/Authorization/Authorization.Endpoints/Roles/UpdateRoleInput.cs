namespace AuthorizationService.Endpoints.Roles;

public sealed record UpdateRoleInput(Guid Id, string Name, int Rank);
