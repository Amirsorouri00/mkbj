using EwaysVNext.Database;
using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound.Persistence;

internal sealed class VariantPriceRepository(EwaysVNextDbContext db) : IVariantPriceRepository
{
    /// <summary>
    /// Overrides are always included: every write path needs to know which currencies are pinned,
    /// and a price loaded without them would quietly answer "none".
    /// </summary>
    private IQueryable<VariantPrice> Full => db.VariantPrices.Include(price => price.Overrides);

    public async Task<VariantPrice?> GetByVariantIdAsync(long variantId, CancellationToken ct = default) =>
        await Full.FirstOrDefaultAsync(price => price.VariantId == variantId, ct);

    public async Task<IReadOnlyList<VariantPrice>> GetByVariantIdsAsync(
        IReadOnlyCollection<long> variantIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(variantIds);
        if (variantIds.Count == 0) return [];

        return await Full.Where(price => variantIds.Contains(price.VariantId)).ToListAsync(ct);
    }

    public async Task AddAsync(VariantPrice price, CancellationToken ct = default) =>
        await db.VariantPrices.AddAsync(price, ct);
}
