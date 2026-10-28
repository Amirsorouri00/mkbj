namespace AuthorizationService.Abstractions.Policies;

public static class AuthorizationPolicyNames
{
    public const string PermissionPrefix = "authz-permission:";

    public static string Permission(string code) => $"{PermissionPrefix}{code}";
}
