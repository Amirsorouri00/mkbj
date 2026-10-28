namespace AuthorizationService.Implementation.Services;

internal static class PermissionServiceMessages
{
    public const string CodeAndNameRequired = "Code and Name are required.";
    public const string CodeTakenFormat = "A permission with code '{0}' already exists.";
    public const string NotPersisted = "Permission was not persisted.";
    public const string NameRequired = "Name is required.";
    public const string NotFoundFormat = "Permission '{0}' was not found.";
    public const string RuleRequired = "At least one rule (role and/or scope combination) is required.";
    public const string UnknownRoleFormat = "'{0}' is not a known role.";
    public const string UnknownScopeFormat = "'{0}' is not a known scope.";
}
