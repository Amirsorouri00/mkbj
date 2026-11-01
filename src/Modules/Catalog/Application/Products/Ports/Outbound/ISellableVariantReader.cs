using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;

/// <summary>
/// The read side of the sellable-variant question. Separate from the inbound port so the
/// service can own the definition of "sellable" while the adapter owns how it is queried.
/// </summary>
public interface ISellableVariantReader
{
    Task<IReadOnlyList<SellableVariant>> ReadSellableAsync(
        IReadOnlyCollection<long>? variantIds, CancellationToken ct = default);

    Task<IReadOnlyDictionary<long, VariantState>> ReadStatesAsync(
        IReadOnlyCollection<long> variantIds, CancellationToken ct = default);
}
