namespace EwaysVNext.Modules.Catalog.QueryApplication.Brands;

/// <summary>
/// A brand as the admin list shows it. Aliases are included because they are the whole reason a
/// name an admin wants may already be taken, and the list is where they find out why.
/// </summary>
public sealed record BrandAdminRow(
    long Id,
    string CanonicalName,
    string DisplayName,
    string Provenance,
    bool IsActive,
    IReadOnlyList<string> Aliases,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
