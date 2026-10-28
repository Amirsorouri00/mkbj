using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Partners.Application.Partners.Domain;

/// <summary>
/// A B2B partner and the keys it holds (spec §5.1).
/// <para>
/// The keys are inside the aggregate rather than beside it because a key's scopes must always be
/// a subset of the partner's. Narrowing a partner has to narrow its keys in the same breath — if
/// those were two aggregates, there would be a moment where a key granted something the partner
/// no longer held, and that moment is exactly when someone uses it.
/// </para>
/// </summary>
public sealed class Partner : AggregateRoot<long>
{
    private const string Validation = "VALIDATION_FAILED";
    private const string NotFound = "NOT_FOUND";
    private const string Conflict = "CONFLICT";

    private readonly List<string> _scopes = [];
    private readonly List<ApiKey> _keys = [];

    private Partner()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public PartnerStatus Status { get; private set; }

    public IReadOnlyList<string> Scopes => _scopes;

    public int RateLimitPerMinute { get; private set; }

    public int BulkMaxItems { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<ApiKey> Keys => _keys;

    public static Partner Create(
        long id,
        string name,
        IReadOnlyCollection<string> scopes,
        int rateLimitPerMinute,
        int bulkMaxItems,
        DateTimeOffset now)
    {
        var partner = new Partner
        {
            Id = id,
            Name = RequireName(name),
            Status = PartnerStatus.Active,
            CreatedAt = now,
        };

        partner.ReplaceScopes(scopes);
        partner.SetLimits(rateLimitPerMinute, bulkMaxItems);

        return partner;
    }

    public void Suspend() => Status = PartnerStatus.Suspended;

    public void Reactivate() => Status = PartnerStatus.Active;

    public void Rename(string name) => Name = RequireName(name);

    /// <summary>
    /// Replaces the partner's scopes and trims every issued key to the new set. Without the trim
    /// the narrowing would be cosmetic: the keys already in the partner's hands would keep
    /// working at the old breadth.
    /// </summary>
    public void SetScopes(IReadOnlyCollection<string> scopes)
    {
        ReplaceScopes(scopes);

        foreach (var key in _keys)
            key.RetainOnly(_scopes);
    }

    public void SetLimits(int rateLimitPerMinute, int bulkMaxItems)
    {
        if (rateLimitPerMinute is < PartnerLimits.RateLimitMin or > PartnerLimits.RateLimitMax)
        {
            throw new DomainException(Validation,
                $"Rate limit must be between {PartnerLimits.RateLimitMin} and {PartnerLimits.RateLimitMax} requests per minute.");
        }

        if (bulkMaxItems is < PartnerLimits.BulkMin or > PartnerLimits.BulkMax)
        {
            throw new DomainException(Validation,
                $"Bulk size must be between {PartnerLimits.BulkMin} and {PartnerLimits.BulkMax} items.");
        }

        RateLimitPerMinute = rateLimitPerMinute;
        BulkMaxItems = bulkMaxItems;
    }

    /// <summary>
    /// Issues a key. A null <c>scopes</c> means every scope the partner holds — the common case,
    /// and spelling it as null rather than making the caller repeat the list keeps the key from
    /// silently falling behind when the partner's scopes change.
    /// </summary>
    public ApiKey IssueKey(
        long keyId,
        string prefix,
        byte[] secretHash,
        IReadOnlyCollection<string>? scopes,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        var granted = scopes is null ? _scopes : Validated(scopes);

        if (granted.Any(scope => !_scopes.Contains(scope, StringComparer.Ordinal)))
        {
            throw new DomainException(Validation,
                "A key cannot be granted a scope the partner does not hold.");
        }

        if (expiresAt is not null && expiresAt <= now)
            throw new DomainException(Validation, "A key's expiry must be in the future.");

        var key = ApiKey.Create(keyId, Id, prefix, secretHash, granted, expiresAt, now);
        _keys.Add(key);

        return key;
    }

    /// <summary>
    /// Issues a replacement and gives the old key a short remaining life rather than killing it
    /// outright. Rotation without that overlap is an outage: the old key would stop the instant
    /// the new one was issued, before the partner had deployed it anywhere.
    /// </summary>
    public ApiKey RotateKey(
        long oldKeyId,
        long newKeyId,
        string prefix,
        byte[] secretHash,
        TimeSpan overlap,
        DateTimeOffset now)
    {
        var old = _keys.SingleOrDefault(key => key.Id == oldKeyId)
            ?? throw new DomainException(NotFound, $"Key {oldKeyId} does not belong to this partner.");

        if (!old.IsUsableAt(now))
        {
            throw new DomainException(Conflict,
                "Only a usable key can be rotated; a revoked or expired key is replaced by issuing a new one.");
        }

        if (overlap < TimeSpan.Zero)
            throw new DomainException(Validation, "The rotation overlap cannot be negative.");

        var replacement = ApiKey.Create(newKeyId, Id, prefix, secretHash, old.Scopes, expiresAt: null, now);
        _keys.Add(replacement);

        old.ExpireNoLaterThan(now + overlap);

        return replacement;
    }

    /// <summary>Idempotent, so a retry after a timeout cannot report a conflict for work that succeeded.</summary>
    public void RevokeKey(long keyId, DateTimeOffset now)
    {
        var key = _keys.SingleOrDefault(k => k.Id == keyId)
            ?? throw new DomainException(NotFound, $"Key {keyId} does not belong to this partner.");

        key.Revoke(now);
    }

    private void ReplaceScopes(IReadOnlyCollection<string> scopes)
    {
        var validated = Validated(scopes);

        _scopes.Clear();
        _scopes.AddRange(validated);
    }

    /// <summary>
    /// Unknown scopes are refused, not dropped. Silently ignoring one would leave an admin
    /// looking at a partner they believe they granted something to.
    /// </summary>
    private static List<string> Validated(IReadOnlyCollection<string> scopes)
    {
        if (scopes is null || scopes.Count == 0)
            throw new DomainException(Validation, "A partner must hold at least one scope.");

        var unknown = scopes.Where(scope => !PartnerScopes.All.Contains(scope)).ToList();
        if (unknown.Count > 0)
        {
            throw new DomainException(Validation,
                $"Unknown scope(s): {string.Join(", ", unknown)}. Valid scopes are {string.Join(", ", PartnerScopes.All)}.");
        }

        return [.. scopes.Distinct(StringComparer.Ordinal)];
    }

    private static string RequireName(string name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed))
            throw new DomainException(Validation, "A partner needs a name.");

        if (trimmed.Length > PartnerLimits.NameMaxLength)
        {
            throw new DomainException(Validation,
                $"A partner name cannot exceed {PartnerLimits.NameMaxLength} characters.");
        }

        return trimmed;
    }
}
