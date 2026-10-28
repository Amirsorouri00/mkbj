namespace AuthorizationService.Implementation.Services;

internal static class ScopeServiceMessages
{
    public const string CodeAndNameRequired = "Code and Name are required.";
    public const string CodeTakenFormat = "A scope with code '{0}' already exists.";
    public const string NameRequired = "Name is required.";
    public const string NotFoundFormat = "Scope '{0}' was not found.";
    public const string InUseFormat = "Scope '{0}' is still referenced by existing permission rules or user assignments.";
}
