using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;

namespace EwaysVNext.Modules.Catalog.Application.Products.Services;

public sealed class SellableVariantService(
    ISellableVariantReader reader,
    ICatalogChangeNotifier notifier) : ISellableVariantPort, IVariantLookupPort
{
    public Task<IReadOnlyList<SellableVariant>> GetSellableAsync(
        IReadOnlyCollection<long>? variantIds = null, CancellationToken ct = default) =>
        reader.ReadSellableAsync(variantIds, ct);

    /// <summary>
    /// The same number <see cref="ICatalogChangeNotifier"/> bumps, so a caller comparing versions
    /// is comparing against the thing that actually changes rather than a second counter that
    /// could drift from it.
    /// </summary>
    public Task<long> GetVersionAsync(CancellationToken ct = default) =>
        notifier.CurrentVersionAsync(ct);

    public Task<IReadOnlyDictionary<long, VariantState>> GetStatesAsync(
        IReadOnlyCollection<long> variantIds, CancellationToken ct = default) =>
        reader.ReadStatesAsync(variantIds, ct);
}
