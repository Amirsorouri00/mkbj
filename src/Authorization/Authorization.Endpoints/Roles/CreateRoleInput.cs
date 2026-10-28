namespace AuthorizationService.Endpoints.Roles;

public sealed record CreateRoleInput(string Code, string Name, int Rank);
