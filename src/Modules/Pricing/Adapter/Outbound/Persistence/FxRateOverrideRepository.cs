using EwaysVNext.Database;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound.Persistence;

internal sealed class FxRateOverrideRepository(EwaysVNextDbContext db) : IFxRateOverrideRepository
{
    public async Task<IReadOnlyList<FxRateOverride>> GetActiveAsync(CancellationToken ct = default) =>
        await db.FxRateOverrides.Where(@override => @override.IsActive).ToListAsync(ct);

    /// <summary>
    /// Tracked, not no-tracking: the caller's next move is usually to replace this row in place,
    /// which is the whole reason there is only ever one of them per pair.
    /// </summary>
    public async Task<FxRateOverride?> GetForPairAsync(
        string @base, string quote, CancellationToken ct = default)
    {
        var normalizedBase = @base?.Trim().ToUpperInvariant() ?? string.Empty;
        var normalizedQuote = quote?.Trim().ToUpperInvariant() ?? string.Empty;

        return await db.FxRateOverrides.FirstOrDefaultAsync(
            @override => @override.Base == normalizedBase && @override.Quote == normalizedQuote, ct);
    }

    public async Task AddAsync(FxRateOverride @override, CancellationToken ct = default) =>
        await db.FxRateOverrides.AddAsync(@override, ct);
}
