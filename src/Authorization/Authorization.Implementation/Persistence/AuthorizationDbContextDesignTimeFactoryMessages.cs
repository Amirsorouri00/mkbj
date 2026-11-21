namespace AuthorizationService.Implementation.Persistence;

internal static class AuthorizationDbContextDesignTimeFactoryMessages
{
    public const string ConnectionStringNotConfigured =
        "ConnectionStrings:Default is not configured. Set it with " +
        "'dotnet user-secrets set \"ConnectionStrings:Default\" \"...\" --project src/Host/Mkbj.Host', " +
        "or the ConnectionStrings__Default environment variable.";
}
