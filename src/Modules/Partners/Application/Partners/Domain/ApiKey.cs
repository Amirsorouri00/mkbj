using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Partners.Application.Partners.Domain;

/// <summary>
/// One issued credential. A child of <see cref="Partner"/>: every state change goes through the
/// root, because whether a key may exist at all depends on the partner's scopes.
/// <para>
/// There is no <c>LastUsedAt</c> column. Writing one on every request would turn a read-only
/// authentication path into a write on a row every concurrent request shares — the usage log
/// already records each call, so the admin read derives it from there instead.
/// </para>
/// </summary>
public sealed class ApiKey : Entity<long>
{
    // A List, not a HashSet, because EF maps a List<string> straight onto a Postgres text[]
    // with no value converter. A converted set breaks the EF InMemory model outright (InMemory
    // composes its own IEnumerable->string converter onto it), which would take the dev-mode
    // fallback and half the test suite with it. Uniqueness is the domain's job either way.
    private readonly List<string> _scopes = [];

    private ApiKey()
    {
    }

    public long PartnerId { get; private set; }

    /// <summary>Stored in clear and indexed: it is a lookup handle, not a secret.</summary>
    public string Prefix { get; private set; } = string.Empty;

    public byte[] SecretHash { get; private set; } = [];

    public IReadOnlyList<string> Scopes => _scopes;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    internal static ApiKey Create(
        long id,
        long partnerId,
        string prefix,
        byte[] secretHash,
        IEnumerable<string> scopes,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        var key = new ApiKey
        {
            Id = id,
            PartnerId = partnerId,
            Prefix = prefix,
            SecretHash = secretHash,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

        key._scopes.AddRange(scopes.Distinct(StringComparer.Ordinal));

        return key;
    }

    /// <summary>
    /// Expiry is exclusive: a key with <c>ExpiresAt = T</c> works up to but not at <c>T</c>, so
    /// "expires at midnight" means it is dead at midnight rather than for one more tick.
    /// </summary>
    public bool IsUsableAt(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || now < ExpiresAt);

    /// <summary>Idempotent: a second revoke must not move the moment the key stopped working.</summary>
    internal void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    /// <summary>
    /// Never extends an existing expiry. Rotation is meant to shorten a key's life, so taking the
    /// earlier of the two keeps a rotation from accidentally granting a key more time than it had.
    /// </summary>
    internal void ExpireNoLaterThan(DateTimeOffset moment) =>
        ExpiresAt = ExpiresAt is null || moment < ExpiresAt ? moment : ExpiresAt;

    /// <summary>Drops anything the partner no longer holds; see <see cref="Partner.SetScopes"/>.</summary>
    internal void RetainOnly(IReadOnlyCollection<string> allowed) =>
        _scopes.RemoveAll(scope => !allowed.Contains(scope));
}
