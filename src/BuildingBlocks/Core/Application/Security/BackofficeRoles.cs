using System.Security.Claims;

namespace EwaysVNext.Core.Application.Security;

/// <summary>
/// Which of a principal's role claims count as backoffice authority.
/// <para>
/// One implementation, because there are two enforcement paths — the REST permission handler and
/// the GraphQL field attribute — and they have already drifted once: the GraphQL path forwarded
/// no roles at all, so every admin GraphQL field refused every caller the moment backoffice
/// authority moved from stored assignments to Keycloak realm roles.
/// </para>
/// </summary>
public static class BackofficeRoles
{
    /// <summary>
    /// The principal's role claims when the token came from the Backoffice realm, otherwise
    /// <c>null</c>. The Shop realm is open self-service registration, so a role named there means
    /// nothing here however it is spelled — forwarding it would let anyone who could influence
    /// their own Shop token claim backoffice authority.
    /// </summary>
    public static IReadOnlyCollection<string>? From(ClaimsPrincipal? user)
    {
        if (user is null || !user.HasClaim(EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm))
            return null;

        var roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();

        return roles.Length == 0 ? null : roles;
    }
}
