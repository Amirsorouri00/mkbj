using EwaysVNext.Modules.Pricing.Application.Fx.Domain;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;

public interface IFxRateRepository
{
    /// <summary>
    /// The most recently fetched rate for each pair. Pricing needs one row per pair and does not
    /// care which provider or day it came from; the table keeps every row it ever had.
    /// </summary>
    Task<IReadOnlyList<FxRate>> GetLatestPerPairAsync(CancellationToken ct = default);

    /// <summary>
    /// The quote currencies already recorded for this base, provider and day — so a refresh can
    /// skip what it already has rather than relying on a unique violation to tell it.
    /// </summary>
    Task<IReadOnlySet<string>> GetExistingQuotesAsync(
        string @base, string provider, DateOnly rateDate, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<FxRate> rates, CancellationToken ct = default);

    Task<FxRate?> GetByIdAsync(long id, CancellationToken ct = default);
}
