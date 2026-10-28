using EwaysVNext.Core.Application.Security;

namespace EwaysVNext.Host.Authentication;

/// <summary>
/// Kept as an alias so the authentication extensions read naturally; the value is owned by
/// <see cref="EwaysClaimTypes"/>, so the two can never drift apart.
/// </summary>
public static class KeycloakClaimTypes
{
    public const string Tenant = EwaysClaimTypes.Tenant;
}
