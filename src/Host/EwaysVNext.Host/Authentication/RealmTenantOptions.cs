namespace EwaysVNext.Host.Authentication;

public sealed class RealmTenantOptions
{
    public required string Realm { get; init; }
    public string? Audience { get; init; }
    public required string ClientId { get; init; }
    public string? RequiredRole { get; init; }
}
