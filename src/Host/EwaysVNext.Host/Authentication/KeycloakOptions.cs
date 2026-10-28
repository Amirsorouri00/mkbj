namespace EwaysVNext.Host.Authentication;

public sealed class KeycloakOptions
{
    public required string InternalUrl { get; init; }
    public required string ExternalUrl { get; init; }
    public required Dictionary<string, RealmTenantOptions> Realms { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(InternalUrl))
            throw new InvalidOperationException(KeycloakOptionsMessages.InternalUrlRequired);
        if (string.IsNullOrWhiteSpace(ExternalUrl))
            throw new InvalidOperationException(KeycloakOptionsMessages.ExternalUrlRequired);
        if (Realms is null || Realms.Count == 0)
            throw new InvalidOperationException(KeycloakOptionsMessages.AtLeastOneRealmRequired);

        foreach (var (tenant, realm) in Realms)
        {
            if (string.IsNullOrWhiteSpace(tenant))
                throw new InvalidOperationException(KeycloakOptionsMessages.TenantKeyRequired);
            if (string.IsNullOrWhiteSpace(realm.Realm))
                throw new InvalidOperationException(string.Format(KeycloakOptionsMessages.RealmNameRequiredFormat, tenant));
            if (string.IsNullOrWhiteSpace(realm.ClientId))
                throw new InvalidOperationException(string.Format(KeycloakOptionsMessages.ClientIdRequiredFormat, tenant));
        }

        var duplicateRealmNames = Realms.Values
            .GroupBy(r => r.Realm, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateRealmNames.Count > 0)
            throw new InvalidOperationException(
                string.Format(KeycloakOptionsMessages.DuplicateRealmNamesFormat, string.Join(", ", duplicateRealmNames)));
    }

    public string IssuerUrl(RealmTenantOptions realm) => $"{ExternalUrl}/realms/{realm.Realm}";
    public string DiscoveryUrl(RealmTenantOptions realm) => $"{InternalUrl}/realms/{realm.Realm}/.well-known/openid-configuration";
}
