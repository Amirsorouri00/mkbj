using System.Globalization;

using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Partners.Application.Partners.Domain;
using EwaysVNext.Modules.Partners.Application.Partners.Keys;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Partners.Application.Partners.Services;

/// <summary>
/// Partner administration (spec §5.1). Each method is load → mutate → audit → one save, and each
/// refuses anyone who is not a super admin before touching anything at all.
/// </summary>
public sealed class PartnerAdminService(
    IPartnerRepository repository,
    ApiKeyHasher hasher,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    IOptions<ApiKeyOptions> options) : IPartnerAdminService
{
    private const string Validation = "VALIDATION_FAILED";

    private readonly ApiKeyOptions _options = options.Value;

    public async Task<Result<long>> CreateAsync(
        CreatePartnerCommand command, AdminActor actor, CancellationToken ct = default)
    {
        if (!actor.IsSuperAdmin)
            return Result.Failure<long>(Error.Forbidden);

        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;
        var id = ids.Generate();

        Partner partner;
        try
        {
            partner = Partner.Create(
                id,
                command.Name,
                command.Scopes,
                command.RateLimitPerMinute ?? PartnerLimits.DefaultRateLimitPerMinute,
                command.BulkMaxItems ?? PartnerLimits.DefaultBulkMaxItems,
                now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<long>(new Error(ex.Code, ex.Message));
        }

        repository.Add(partner);

        audit.Record(actor.ToAudit(), "partner.created", nameof(Partner), id.ToString(CultureInfo.InvariantCulture),
            before: null,
            after: new { partner.Name, Scopes = partner.Scopes.Order(), partner.RateLimitPerMinute, partner.BulkMaxItems });

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(id);
    }

    public Task<Result> SuspendAsync(long partnerId, AdminActor actor, CancellationToken ct = default) =>
        MutateAsync(partnerId, actor, "partner.suspended",
            partner => (Before: new { partner.Status }, Apply: () => partner.Suspend()), ct);

    public Task<Result> ReactivateAsync(long partnerId, AdminActor actor, CancellationToken ct = default) =>
        MutateAsync(partnerId, actor, "partner.reactivated",
            partner => (Before: new { partner.Status }, Apply: () => partner.Reactivate()), ct);

    public Task<Result> SetScopesAsync(
        long partnerId, IReadOnlyCollection<string> scopes, AdminActor actor, CancellationToken ct = default) =>
        MutateAsync(partnerId, actor, "partner.scopes_set",
            partner => (Before: new { Scopes = partner.Scopes.Order().ToArray() },
                        Apply: () => partner.SetScopes(scopes)), ct);

    public Task<Result> SetLimitsAsync(
        long partnerId, int rateLimitPerMinute, int bulkMaxItems, AdminActor actor, CancellationToken ct = default) =>
        MutateAsync(partnerId, actor, "partner.limits_set",
            partner => (Before: new { partner.RateLimitPerMinute, partner.BulkMaxItems },
                        Apply: () => partner.SetLimits(rateLimitPerMinute, bulkMaxItems)), ct);

    public async Task<Result<IssuedApiKey>> IssueKeyAsync(
        long partnerId,
        IReadOnlyCollection<string>? scopes,
        DateTimeOffset? expiresAt,
        AdminActor actor,
        CancellationToken ct = default)
    {
        if (!actor.IsSuperAdmin)
            return Result.Failure<IssuedApiKey>(Error.Forbidden);

        var partner = await repository.GetAsync(partnerId, ct);
        if (partner is null)
            return Result.Failure<IssuedApiKey>(Error.NotFound);

        var generated = ApiKeyFormat.Generate();
        var now = clock.UtcNow;

        ApiKey key;
        try
        {
            key = partner.IssueKey(
                ids.Generate(), generated.Prefix, hasher.Hash(generated.Secret), scopes, expiresAt, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<IssuedApiKey>(new Error(ex.Code, ex.Message));
        }

        RecordKeyAction(actor, "partner.key_issued", partnerId, key);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(Issued(key, generated.Plaintext));
    }

    public async Task<Result<IssuedApiKey>> RotateKeyAsync(
        long partnerId, long keyId, TimeSpan? overlap, AdminActor actor, CancellationToken ct = default)
    {
        if (!actor.IsSuperAdmin)
            return Result.Failure<IssuedApiKey>(Error.Forbidden);

        var effectiveOverlap = overlap ?? _options.RotationOverlap;

        // Checked before loading: an overlap is a second live credential for its whole duration,
        // so "rotated" with a year's grace has not been rotated.
        if (effectiveOverlap < TimeSpan.Zero || effectiveOverlap > _options.MaxRotationOverlap)
        {
            return Result.Failure<IssuedApiKey>(new Error(Validation,
                $"The rotation overlap must be between zero and {_options.MaxRotationOverlap}."));
        }

        var partner = await repository.GetAsync(partnerId, ct);
        if (partner is null)
            return Result.Failure<IssuedApiKey>(Error.NotFound);

        var generated = ApiKeyFormat.Generate();

        ApiKey replacement;
        try
        {
            replacement = partner.RotateKey(
                keyId, ids.Generate(), generated.Prefix, hasher.Hash(generated.Secret),
                effectiveOverlap, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<IssuedApiKey>(new Error(ex.Code, ex.Message));
        }

        RecordKeyAction(actor, "partner.key_rotated", partnerId, replacement,
            before: new { RotatedKeyId = keyId, Overlap = effectiveOverlap });

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(Issued(replacement, generated.Plaintext));
    }

    public Task<Result> RevokeKeyAsync(long partnerId, long keyId, AdminActor actor, CancellationToken ct = default) =>
        MutateAsync(partnerId, actor, "partner.key_revoked",
            partner => (Before: new { KeyId = keyId },
                        Apply: () => partner.RevokeKey(keyId, clock.UtcNow)), ct);

    /// <summary>
    /// The shape every single-aggregate mutation shares: refuse a non-super-admin, load, apply,
    /// audit the before and after in the same unit of work, and save once.
    /// </summary>
    private async Task<Result> MutateAsync(
        long partnerId,
        AdminActor actor,
        string action,
        Func<Partner, (object Before, Action Apply)> describe,
        CancellationToken ct)
    {
        if (!actor.IsSuperAdmin)
            return Result.Failure(Error.Forbidden);

        var partner = await repository.GetAsync(partnerId, ct);
        if (partner is null)
            return Result.Failure(Error.NotFound);

        var (before, apply) = describe(partner);

        try
        {
            apply();
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(ex.Code, ex.Message));
        }

        audit.Record(actor.ToAudit(), action, nameof(Partner), partnerId.ToString(CultureInfo.InvariantCulture),
            before, After(partner));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static object After(Partner partner) => new
    {
        partner.Status,
        Scopes = partner.Scopes.Order().ToArray(),
        partner.RateLimitPerMinute,
        partner.BulkMaxItems,
    };

    /// <summary>
    /// Records the key's identity and grant — never the plaintext and never the hash. An audit
    /// log is read by more people, and kept longer, than any credential should be.
    /// </summary>
    private void RecordKeyAction(AdminActor actor, string action, long partnerId, ApiKey key, object? before = null) =>
        audit.Record(actor.ToAudit(), action, nameof(Partner), partnerId.ToString(CultureInfo.InvariantCulture),
            before,
            after: new { KeyId = key.Id, key.Prefix, Scopes = key.Scopes.Order().ToArray(), key.ExpiresAt });

    private static IssuedApiKey Issued(ApiKey key, string plaintext) =>
        new(key.Id, key.Prefix, plaintext, [.. key.Scopes.Order()], key.CreatedAt, key.ExpiresAt);
}
