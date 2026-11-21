using Mkbj.Database;
using Mkbj.Modules.Catalog.Application.Categories.Ports.Outbound;
using Mkbj.Modules.Catalog.Application.Products.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mkbj.Modules.Catalog.Adapter.Outbound.Persistence.Categories;

/// <summary>
/// Anti-corruption layer: Categories learn only the active-existence fact, not the product's
/// persistence model or schema.
/// </summary>
internal sealed class CatalogProductLookup(MkbjDbContext db) : ICatalogProductLookup
{
    public Task<bool> ExistsAndIsActiveAsync(long productId, CancellationToken ct = default)
        => db.Products.AnyAsync(
            product => product.Id == productId && product.Status == ProductStatus.Active, ct);
}
