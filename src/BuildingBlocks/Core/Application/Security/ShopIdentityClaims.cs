using System.Security.Claims;

namespace EwaysVNext.Core.Application.Security;

/// <summary>Who is shopping, as the token says.</summary>
public readonly record struct ShopIdentity(Guid IdentityUserId, string Email, bool EmailVerified);

/// <summary>
/// Reads the three things a shop request needs from the token, or refuses.
/// </summary>
/// <remarks>
/// The tenant check is made again here although a policy already made it. The policy decides
/// whether the request may proceed; this decides <b>whose</b> records to touch, and a bug that
/// let a Backoffice token through would otherwise attach a cart or an order to an administrator.
/// </remarks>
public static class ShopIdentityClaims
{
    public static ShopIdentity? Read(ClaimsPrincipal? user)
    {
        if (user is null || !user.HasClaim(EwaysClaimTypes.Tenant, AdminRoles.ShopRealm))
            return null;

        var subject = user.FindFirst(EwaysClaimTypes.Subject)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(subject, out var identityUserId)) return null;

        var email = user.FindFirst(EwaysClaimTypes.Email)?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrWhiteSpace(email)) return null;

        var verified = string.Equals(
            user.FindFirst(EwaysClaimTypes.EmailVerified)?.Value,
            "true",
            StringComparison.OrdinalIgnoreCase);

        return new ShopIdentity(identityUserId, email, verified);
    }
}
