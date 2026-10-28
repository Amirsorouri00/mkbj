namespace EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;

/// <summary>
/// What a verified key grants. The limits ride along so the rate limiter does not need a second
/// database round trip on the hot path of every partner request.
/// </summary>
public sealed record ApiKeyAuthResult(
    long PartnerId,
    long ApiKeyId,
    IReadOnlySet<string> Scopes,
    int RateLimitPerMinute,
    int BulkMaxItems);

/// <summary>
/// Verifies a presented partner key.
/// <para>
/// It returns <c>null</c> for every failure and never says which one. A caller holding a wrong
/// key learns only that it did not work — not whether the prefix exists, whether the partner is
/// suspended, or whether the key has expired. Each of those would be a probe. The reason is
/// logged server-side, where an operator can see it.
/// </para>
/// </summary>
public interface IApiKeyAuthenticator
{
    Task<ApiKeyAuthResult?> AuthenticateAsync(string presentedKey, CancellationToken ct = default);
}
