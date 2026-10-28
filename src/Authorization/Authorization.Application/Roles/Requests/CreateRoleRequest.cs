namespace AuthorizationService.Application.Roles.Requests;

public record CreateRoleRequest(string Code, string Name, int Rank);
