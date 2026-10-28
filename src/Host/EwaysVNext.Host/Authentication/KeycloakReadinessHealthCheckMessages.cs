namespace EwaysVNext.Host.Authentication;

internal static class KeycloakReadinessHealthCheckMessages
{
    public const string Healthy = "Keycloak signing keys loaded for all realm tenants.";
    public const string UnhealthyFormat = "Could not load Keycloak configuration/signing keys for: {0}.";
    public const string NoSigningKeysFormat = "{0} (no signing keys)";
    public const string ExceptionFormat = "{0} ({1})";
}
