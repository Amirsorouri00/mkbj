using EwaysVNext.Modules.Pricing.Application.Fx.Domain;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;

public interface IFxRateOverrideRepository
{
    Task<IReadOnlyList<FxRateOverride>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// The pair's row whether it is active or not. Setting an override has to find a withdrawn
    /// one and revive it, because the unique index allows only one row per pair.
    /// </summary>
    Task<FxRateOverride?> GetForPairAsync(string @base, string quote, CancellationToken ct = default);

    Task AddAsync(FxRateOverride @override, CancellationToken ct = default);
}
