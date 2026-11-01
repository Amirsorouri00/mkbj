using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Common;

namespace EwaysVNext.Modules.Catalog.Application.Brands.Domain;

/// <summary>
/// A manufacturer or marque a product is sold under.
/// <para>
/// Unlike most of the catalogue, a brand is created by an import as often as by an admin — the
/// same company arrives as "Northwind", "North Wind" and "NORTHWIND Ltd." across three feeds. The
/// canonical name is what makes those one brand, and the alias list is where the spellings the
/// normalizer will not guess at are recorded.
/// </para>
/// </summary>
public sealed class Brand : AggregateRoot<long>
{
    /// <summary>
    /// How this brand came to exist. A closed set, because it decides whether a later import may
    /// overwrite what an admin typed.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownProvenances =
        new HashSet<string>(StringComparer.Ordinal) { "admin", "import", "seed" };

    private readonly List<BrandAlias> _aliases = [];

    /// <summary>
    /// Derived from <see cref="DisplayName"/> and unique across the catalogue. It is what a feed's
    /// brand column is matched against, so it is never set apart from the name.
    /// </summary>
    public string CanonicalName { get; private set; } = string.Empty;

    /// <summary>The brand as it is shown to a customer, with its own capitalisation.</summary>
    public string DisplayName { get; private set; } = null!;

    public string Provenance { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<BrandAlias> Aliases => _aliases.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    private Brand() { }

    public static Brand Create(long id, string displayName, string provenance, DateTimeOffset now)
    {
        if (id <= 0)
        {
            throw new DomainException("BRAND_ID_INVALID", "Brand ID must be positive.");
        }

        if (provenance is null || !KnownProvenances.Contains(provenance))
        {
            throw new DomainException(
                "BRAND_PROVENANCE_INVALID",
                $"Brand provenance must be one of: {string.Join(", ", KnownProvenances)}.");
        }

        var normalizedDisplayName = NormalizeDisplayName(displayName);

        return new Brand
        {
            Id = id,
            DisplayName = normalizedDisplayName,
            CanonicalName = CanonicalKey.From(normalizedDisplayName),
            Provenance = provenance,
            IsActive = true,
            CreatedAt = now
        };
    }

    public void Rename(string displayName, DateTimeOffset now)
    {
        var normalizedDisplayName = NormalizeDisplayName(displayName);
        var canonicalName = CanonicalKey.From(normalizedDisplayName);

        if (DisplayName == normalizedDisplayName && CanonicalName == canonicalName)
        {
            return;
        }

        DisplayName = normalizedDisplayName;
        CanonicalName = canonicalName;

        // An alias equal to the new canonical name matches nothing the name does not already
        // match, and the alias index is global — leaving it would hand the next save a duplicate.
        _aliases.RemoveAll(alias => string.Equals(alias.Alias, canonicalName, StringComparison.Ordinal));

        UpdatedAt = now;
    }

    /// <summary>
    /// Records another spelling of this brand. A spelling already held, or one equal to the
    /// canonical name, is a no-op rather than an error: aliases arrive from imports in bulk and
    /// re-importing a feed must not fail on what it already produced.
    /// </summary>
    public void AddAlias(long aliasId, string alias, DateTimeOffset now)
    {
        if (aliasId <= 0)
        {
            throw new DomainException("BRAND_ALIAS_ID_INVALID", "Brand alias ID must be positive.");
        }

        if (!CanonicalKey.TryFrom(alias, out var canonical))
            return;

        if (string.Equals(canonical, CanonicalName, StringComparison.Ordinal) ||
            _aliases.Any(existing => string.Equals(existing.Alias, canonical, StringComparison.Ordinal)))
            return;

        _aliases.Add(new BrandAlias(aliasId, Id, canonical));
        UpdatedAt = now;
    }

    /// <summary>
    /// Removes an alias given in any spelling, since the caller is as likely to be holding the
    /// supplier's wording as the stored form.
    /// </summary>
    public void RemoveAlias(string alias, DateTimeOffset now)
    {
        if (!CanonicalKey.TryFrom(alias, out var canonical))
            return;

        if (_aliases.RemoveAll(existing => string.Equals(existing.Alias, canonical, StringComparison.Ordinal)) > 0)
        {
            UpdatedAt = now;
        }
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        if (IsActive == active)
            return;

        IsActive = active;
        UpdatedAt = now;
    }

    private static string NormalizeDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("BRAND_NAME_REQUIRED", "Brand name is required.");
        }

        var normalized = displayName.Trim();
        if (normalized.Length > 200)
        {
            throw new DomainException("BRAND_NAME_TOO_LONG", "Brand name cannot exceed 200 characters.");
        }

        return normalized;
    }
}
