namespace EwaysVNext.Modules.Partners.Application.Partners.Ports.Outbound;

/// <summary>
/// Everything authentication needs about one key, flattened. The partner's status and limits are
/// carried here rather than loaded separately because this runs on every partner request.
/// </summary>
public sealed record ApiKeyCredential(
    long KeyId,
    long PartnerId,
    byte[] SecretHash,
    IReadOnlyList<string> KeyScopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    bool PartnerActive,
    int RateLimitPerMinute,
    int BulkMaxItems);

/// <summary>
/// The read side of authentication: one indexed lookup on the key's clear prefix, projected
/// without tracking. Deliberately not <c>IPartnerRepository</c> — loading an aggregate on every
/// request would track it for a change that never comes.
/// </summary>
public interface IApiKeyLookup
{
    Task<ApiKeyCredential?> FindByPrefixAsync(string prefix, CancellationToken ct = default);
}
