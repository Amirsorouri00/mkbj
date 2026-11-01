using EwaysVNext.Database;
using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;
using EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound.Persistence;

internal sealed class CurrencyRepository(EwaysVNextDbContext db) : ICurrencyRepository
{
    public async Task<IReadOnlyList<Currency>> GetAllAsync(CancellationToken ct = default) =>
        await db.Currencies.OrderBy(currency => currency.Code).ToListAsync(ct);

    public async Task<Currency?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var normalized = code.Trim().ToUpperInvariant();
        return await db.Currencies.FirstOrDefaultAsync(currency => currency.Code == normalized, ct);
    }
}
