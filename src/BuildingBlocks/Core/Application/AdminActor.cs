using System.Security.Claims;

using EwaysVNext.Audit;
using EwaysVNext.Core.Application.Security;

namespace EwaysVNext.Core.Application;

/// <summary>
/// The authenticated backoffice user behind an admin action (spec §6.1). Two roles only: staff,
/// and super admin, who alone may touch partners, FX, currencies, settings and refunds.
/// </summary>
public sealed record AdminActor(Guid UserId, bool IsSuperAdmin)
{
    public AuditActor ToAudit() => new(UserId, IsSuperAdmin ? AdminRoles.SuperAdmin : AdminRoles.Staff);

    /// <summary>
    /// Reads an actor from a validated token, or refuses.
    /// <para>
    /// The tenant check is the one that matters. The Shop realm is open self-service
    /// registration, so a role claim minted there carries no backoffice meaning whatever it is
    /// called — without this check, anyone who could influence their own Shop token would be an
    /// admin. A token from any other realm, or with no realm, is refused outright.
    /// </para>
    /// </summary>
    public static bool TryFrom(ClaimsPrincipal user, out AdminActor? actor)
    {
        actor = null;

        var tenant = user.FindFirst(EwaysClaimTypes.Tenant)?.Value;
        if (!string.Equals(tenant, AdminRoles.BackofficeRealm, StringComparison.Ordinal))
            return false;

        // The JWT handler maps "sub" to NameIdentifier under the default inbound claim map, so
        // the raw name may be gone by the time a principal reaches here.
        var subject = user.FindFirst(EwaysClaimTypes.Subject)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(subject, out var userId))
            return false;

        var isSuperAdmin = user.HasClaim(ClaimTypes.Role, AdminRoles.SuperAdmin);
        if (!isSuperAdmin && !user.HasClaim(ClaimTypes.Role, AdminRoles.Staff))
            return false;

        actor = new AdminActor(userId, isSuperAdmin);
        return true;
    }
}
