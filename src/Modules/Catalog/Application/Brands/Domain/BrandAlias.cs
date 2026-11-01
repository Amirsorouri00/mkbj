using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.Brands.Domain;

/// <summary>
/// Another name the same brand trades under. The value is already canonical — the aggregate
/// normalises before constructing one — because the uniqueness index is global and an
/// un-normalised row would silently claim a name nothing ever looks up.
/// </summary>
public sealed class BrandAlias : Entity<long>
{
    public long BrandId { get; private set; }

    public string Alias { get; private set; } = null!;

    private BrandAlias() { }

    internal BrandAlias(long id, long brandId, string alias)
    {
        Id = id;
        BrandId = brandId;
        Alias = alias;
    }
}
