namespace AuthorizationService.Application.Entities;

public class InvalidPermissionRuleException(string message) : Exception(message);
