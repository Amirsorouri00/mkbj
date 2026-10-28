namespace AuthorizationService.Implementation.Services;

internal static class RoleServiceMessages
{
    public const string CodeAndNameRequired = "Code and Name are required.";
    public const string CodeTakenFormat = "A role with code '{0}' already exists.";
    public const string NameRequired = "Name is required.";
    public const string NotFoundFormat = "Role '{0}' was not found.";
    public const string InUseFormat = "Role '{0}' is still referenced by existing permission rules or user assignments.";
}
