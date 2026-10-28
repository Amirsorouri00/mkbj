namespace AuthorizationService.Implementation.Services;

internal static class UserRoleScopeServiceMessages
{
    public const string UnknownScopeFormat = "'{0}' is not a known scope.";
    public const string NoRolesExist = "No roles exist yet; create at least one role first.";
    public const string AdminAlreadyExistsFormat = "An assignment for role '{0}' already exists; use the normal assignment endpoint instead.";
    public const string UnknownRoleFormat = "'{0}' is not a known role.";
    public const string AssignmentTaken = "This user already has this role in this scope.";
    public const string AssignmentNotFoundFormat = "Assignment '{0}' was not found.";
}
