using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound.Gateways;

/// <summary>
/// Anti-corruption layer: Pricing learns one fact from Catalog, not Catalog's shape.
/// </summary>
internal sealed class CatalogVariantGateway(IVariantLookupPort variants) : ICatalogVariantGateway
{
    public async Task<bool> VariantExistsAsync(long variantId, CancellationToken ct = default)
    {
        var states = await variants.GetStatesAsync([variantId], ct);
        return states.TryGetValue(variantId, out var state) && state.Exists;
    }
}
