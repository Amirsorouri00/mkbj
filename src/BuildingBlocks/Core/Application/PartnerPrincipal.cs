using System.Globalization;
using System.Security.Claims;

namespace EwaysVNext.Core.Application;

/// <summary>
/// The authenticated partner behind a request.
/// <para>
/// It is built <b>only</b> from claims the API-key handler produced, never deserialized from a
/// request body (spec §6.2). That is the whole point: with no <c>partnerId</c> property to bind,
/// one partner cannot name another and read its orders — an architecture test enforces the
/// absence, and every partner-facing query filters on this id in SQL.
/// </para>
/// </summary>
public sealed record PartnerPrincipal(long PartnerId, long ApiKeyId, IReadOnlySet<string> Scopes)
{
    public const string PartnerIdClaim = "partner_id";
    public const string ApiKeyIdClaim = "api_key_id";
    public const string ScopeClaim = "scope";

    public static bool TryFrom(ClaimsPrincipal user, out PartnerPrincipal? principal)
    {
        principal = null;

        if (!TryPositiveLong(user.FindFirst(PartnerIdClaim)?.Value, out var partnerId)) return false;
        if (!TryPositiveLong(user.FindFirst(ApiKeyIdClaim)?.Value, out var apiKeyId)) return false;

        // Unknown scope strings are dropped rather than carried: an unrecognised scope must never
        // be mistaken for a granted one by a later `Has` check.
        var scopes = user.FindAll(ScopeClaim)
            .Select(claim => claim.Value)
            .Where(PartnerScopes.All.Contains)
            .ToHashSet(StringComparer.Ordinal);

        principal = new PartnerPrincipal(partnerId, apiKeyId, scopes);
        return true;
    }

    public bool Has(string scope) => Scopes.Contains(scope);

    private static bool TryPositiveLong(string? value, out long parsed) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;
}
