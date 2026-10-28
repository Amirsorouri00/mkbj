namespace AuthorizationService.Implementation.Services;

internal static class AuthorizationCheckServiceMessages
{
    public const string PermissionDoesNotExistFormat = "Permission '{0}' does not exist.";
    public const string Granted = "Granted.";
    public const string NoSatisfyingAssignment = "User has no role/scope assignment satisfying this permission.";
}
