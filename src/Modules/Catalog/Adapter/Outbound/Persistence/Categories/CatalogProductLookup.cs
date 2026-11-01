using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Categories;

/// <summary>
/// Anti-corruption layer: Categories learn only the active-existence fact, not the product's
/// persistence model or schema.
/// </summary>
internal sealed class CatalogProductLookup(EwaysVNextDbContext db) : ICatalogProductLookup
{
    public Task<bool> ExistsAndIsActiveAsync(long productId, CancellationToken ct = default)
        => db.Products.AnyAsync(
            product => product.Id == productId && product.Status == ProductStatus.Active, ct);
}
