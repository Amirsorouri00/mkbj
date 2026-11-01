using EwaysVNext.Database;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound.Persistence;

internal sealed class FxRateRepository(EwaysVNextDbContext db) : IFxRateRepository
{
    public async Task<IReadOnlyList<FxRate>> GetLatestPerPairAsync(CancellationToken ct = default) =>
        await db.FxRates.AsNoTracking()
            .GroupBy(rate => new { rate.Base, rate.Quote })
            .Select(pair => pair.OrderByDescending(rate => rate.FetchedAt).First())
            .ToListAsync(ct);

    public async Task<IReadOnlySet<string>> GetExistingQuotesAsync(
        string @base, string provider, DateOnly rateDate, CancellationToken ct = default)
    {
        var normalizedBase = @base?.Trim().ToUpperInvariant() ?? string.Empty;
        var normalizedProvider = provider?.Trim() ?? string.Empty;

        var quotes = await db.FxRates.AsNoTracking()
            .Where(rate => rate.Base == normalizedBase
                && rate.Provider == normalizedProvider
                && rate.RateDate == rateDate)
            .Select(rate => rate.Quote)
            .ToListAsync(ct);

        return quotes.ToHashSet(StringComparer.Ordinal);
    }

    public async Task AddRangeAsync(IEnumerable<FxRate> rates, CancellationToken ct = default) =>
        await db.FxRates.AddRangeAsync(rates, ct);

    public async Task<FxRate?> GetByIdAsync(long id, CancellationToken ct = default) =>
        await db.FxRates.FirstOrDefaultAsync(rate => rate.Id == id, ct);
}
