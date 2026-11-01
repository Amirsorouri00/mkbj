using System.Security.Claims;

using EwaysVNext.Core.Application.Security;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Shop;

/// <summary>
/// Reads the three things a shop request needs from the token, or refuses.
/// <para>
/// The tenant check is repeated here even though the policy already made it. The policy decides
/// whether the request may proceed; this decides whose record to touch — and a bug that let a
/// Backoffice token through would otherwise create a customer against an admin's subject id.
/// </para>
/// </summary>
public static class ShopIdentity
{
    public static bool TryRead(ClaimsPrincipal user, out Guid subject, out string email, out bool emailVerified)
    {
        subject = Guid.Empty;
        email = string.Empty;
        emailVerified = false;

        if (user is null || !user.HasClaim(EwaysClaimTypes.Tenant, AdminRoles.ShopRealm))
            return false;

        var subjectClaim = user.FindFirst(EwaysClaimTypes.Subject)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(subjectClaim, out subject))
            return false;

        email = user.FindFirst(EwaysClaimTypes.Email)?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email))
            return false;

        emailVerified = string.Equals(
            user.FindFirst(EwaysClaimTypes.EmailVerified)?.Value, "true", StringComparison.OrdinalIgnoreCase);

        return true;
    }
}
