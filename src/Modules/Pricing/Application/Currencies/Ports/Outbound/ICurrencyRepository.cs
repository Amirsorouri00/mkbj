using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;

namespace EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;

public interface ICurrencyRepository
{
    /// <summary>
    /// Every currency, enabled or not. The set is five rows; narrowing it in the repository would
    /// only move the filter somewhere the caller cannot see.
    /// </summary>
    Task<IReadOnlyList<Currency>> GetAllAsync(CancellationToken ct = default);

    Task<Currency?> GetByCodeAsync(string code, CancellationToken ct = default);
}
