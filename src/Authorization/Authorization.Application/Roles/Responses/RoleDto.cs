namespace AuthorizationService.Application.Roles.Responses;

public record RoleDto(Guid Id, string Code, string Name, int Rank);
