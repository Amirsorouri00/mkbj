namespace AuthorizationService.Abstractions.Authorization.Responses;

public record CheckAuthorizationResult(bool Allowed, string Reason);
