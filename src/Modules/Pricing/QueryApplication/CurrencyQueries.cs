using EwaysVNext.Database;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Pricing.QueryApplication;

public sealed class CurrencyQueries(EwaysVNextDbContext db) : ICurrencyQueries
{
    public async Task<IReadOnlyList<CurrencyDto>> GetEnabledAsync(CancellationToken ct = default) =>
        await db.Currencies.AsNoTracking()
            .Where(currency => currency.IsEnabled)
            .OrderBy(currency => currency.Code)
            .Select(currency => new CurrencyDto(currency.Code, currency.MinorUnits))
            .ToListAsync(ct);

    public async Task<bool> IsEnabledAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var normalized = code.Trim().ToUpperInvariant();
        return await db.Currencies.AsNoTracking()
            .AnyAsync(currency => currency.IsEnabled && currency.Code == normalized, ct);
    }
}
