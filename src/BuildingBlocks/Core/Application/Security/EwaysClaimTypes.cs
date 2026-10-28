namespace EwaysVNext.Core.Application.Security;

/// <summary>
/// Every claim name the system reads, in one place. They are spelled as they arrive on the wire —
/// Keycloak's <c>sub</c>, our own <c>tenant</c>, the partner handler's <c>partner_id</c> — rather
/// than as the long ASP.NET Core URIs, because a mismatch between the string a token carries and
/// the string a check looks for fails open-looking: the claim is simply absent, and the caller
/// reads as unauthenticated rather than as misconfigured.
/// </summary>
public static class EwaysClaimTypes
{
    /// <summary>Which Keycloak realm issued the token: <c>Shop</c> or <c>Backoffice</c>.</summary>
    public const string Tenant = "tenant";

    public const string Subject = "sub";
    public const string Email = "email";
    public const string EmailVerified = "email_verified";

    public const string PartnerId = "partner_id";
    public const string ApiKeyId = "api_key_id";
    public const string Scope = "scope";
    public const string RateLimitPerMinute = "rate_limit_per_minute";
    public const string BulkMaxItems = "bulk_max_items";
}
