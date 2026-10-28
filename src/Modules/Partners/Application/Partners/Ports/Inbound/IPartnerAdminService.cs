using EwaysVNext.Core.Application;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;

/// <summary>Null limits take the spec defaults rather than meaning "no limit".</summary>
public sealed record CreatePartnerCommand(
    string Name,
    IReadOnlyCollection<string> Scopes,
    int? RateLimitPerMinute,
    int? BulkMaxItems);

/// <summary>
/// A freshly issued key. <see cref="PlaintextKey"/> is the only time the full key exists outside
/// the caller's hands — it is not stored and cannot be recovered, so an admin who loses it issues
/// another.
/// </summary>
public sealed record IssuedApiKey(
    long KeyId,
    string Prefix,
    string PlaintextKey,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Managing partners and their credentials (spec §5.1).
/// <para>
/// Every method is super-admin only. Issuing a key is handing out the ability to place orders in
/// the seller's name, and widening a partner's scopes does the same more quietly — neither is
/// day-to-day shop work. Each call is audited in the same save as the change it describes.
/// </para>
/// </summary>
public interface IPartnerAdminService
{
    Task<Result<long>> CreateAsync(CreatePartnerCommand command, AdminActor actor, CancellationToken ct = default);

    Task<Result> SuspendAsync(long partnerId, AdminActor actor, CancellationToken ct = default);

    Task<Result> ReactivateAsync(long partnerId, AdminActor actor, CancellationToken ct = default);

    Task<Result> SetScopesAsync(long partnerId, IReadOnlyCollection<string> scopes, AdminActor actor, CancellationToken ct = default);

    Task<Result> SetLimitsAsync(long partnerId, int rateLimitPerMinute, int bulkMaxItems, AdminActor actor, CancellationToken ct = default);

    Task<Result<IssuedApiKey>> IssueKeyAsync(long partnerId, IReadOnlyCollection<string>? scopes, DateTimeOffset? expiresAt, AdminActor actor, CancellationToken ct = default);

    Task<Result<IssuedApiKey>> RotateKeyAsync(long partnerId, long keyId, TimeSpan? overlap, AdminActor actor, CancellationToken ct = default);

    Task<Result> RevokeKeyAsync(long partnerId, long keyId, AdminActor actor, CancellationToken ct = default);
}
