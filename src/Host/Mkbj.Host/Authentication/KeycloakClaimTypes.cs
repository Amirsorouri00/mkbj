using Mkbj.Core.Application.Security;

namespace Mkbj.Host.Authentication;

/// <summary>
/// Kept as an alias so the authentication extensions read naturally; the value is owned by
/// <see cref="MkbjClaimTypes"/>, so the two can never drift apart.
/// </summary>
public static class KeycloakClaimTypes
{
    public const string Tenant = MkbjClaimTypes.Tenant;
}
