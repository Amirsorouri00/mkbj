using EwaysVNext.Modules.Pricing.Application.Prices.Domain;

namespace EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;

/// <summary>
/// Loads carry their overrides. Every write path consults them — setting a base price has to know
/// which currencies are pinned — and a price loaded without them cannot answer that.
/// </summary>
public interface IVariantPriceRepository
{
    Task<VariantPrice?> GetByVariantIdAsync(long variantId, CancellationToken ct = default);

    Task<IReadOnlyList<VariantPrice>> GetByVariantIdsAsync(
        IReadOnlyCollection<long> variantIds, CancellationToken ct = default);

    Task AddAsync(VariantPrice price, CancellationToken ct = default);
}
