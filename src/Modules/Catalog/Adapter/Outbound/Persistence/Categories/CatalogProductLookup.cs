using EwaysVNext.Modules.Catalog.Contracts.Products;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Categories;

/// <summary>
/// Anti-corruption layer: Categories learn only the active-existence fact,
/// not Catalog's persistence model or schema.
/// </summary>
internal sealed class CatalogProductLookup(IProductAvailabilityContract products) : ICatalogProductLookup
{
    public Task<bool> ExistsAndIsActiveAsync(long productId, CancellationToken ct = default) =>
        products.ExistsAndIsActiveAsync(productId, ct);
}
