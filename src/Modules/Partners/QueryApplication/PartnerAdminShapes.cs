namespace EwaysVNext.Modules.Partners.QueryApplication;

public sealed record PartnerListItem(
    long Id,
    string Name,
    string Status,
    IReadOnlyList<string> Scopes,
    int RateLimitPerMinute,
    int BulkMaxItems,
    int ActiveKeyCount,
    DateTimeOffset CreatedAt);

/// <summary>
/// A key as an admin sees it. Carries the prefix, never the secret or its hash — there is nothing
/// stored that could reconstruct the key, and this shape is the reason it stays that way.
/// <para>
/// <c>Status</c> is derived at read time from <c>RevokedAt</c>, <c>ExpiresAt</c> and the current
/// moment rather than stored: a stored status would need a job to turn keys Expired on a schedule
/// and would be wrong in between runs.
/// </para>
/// <para>
/// <c>LastUsedAt</c> is the newest usage row for this key, or null if it has never been used.
/// Derived rather than written on the authentication path, which would turn a read into a write
/// on a row every concurrent request for that key shares.
/// </para>
/// </summary>
public sealed record ApiKeyView(
    long Id,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    string Status,
    DateTimeOffset? LastUsedAt);

public sealed record PartnerDetail(PartnerListItem Partner, IReadOnlyList<ApiKeyView> Keys);

public sealed record ApiKeyUsageItem(
    long KeyId,
    DateTimeOffset At,
    string Method,
    string Path,
    int Status,
    string CorrelationId,
    int DurationMs);

public static class ApiKeyStatuses
{
    public const string Active = "Active";
    public const string Expired = "Expired";
    public const string Revoked = "Revoked";
}
