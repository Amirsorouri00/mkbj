namespace EwaysVNext.Host.Authentication;

internal static class KeycloakOptionsMessages
{
    public const string InternalUrlRequired = "Keycloak:InternalUrl is required.";
    public const string ExternalUrlRequired = "Keycloak:ExternalUrl is required.";
    public const string AtLeastOneRealmRequired = "Keycloak:Realms must configure at least one realm tenant.";
    public const string TenantKeyRequired = "Keycloak:Realms tenant keys must be non-empty.";
    public const string RealmNameRequiredFormat = "Keycloak:Realms:{0}:Realm is required.";
    public const string ClientIdRequiredFormat = "Keycloak:Realms:{0}:ClientId is required.";
    public const string DuplicateRealmNamesFormat = "Keycloak:Realms must map to distinct Keycloak realms; duplicated: {0}.";
}
