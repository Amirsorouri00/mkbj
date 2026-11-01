using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound.Gateways;

/// <summary>
/// Anti-corruption layer: Pricing learns one duration from Settings, not Settings' shape.
/// </summary>
internal sealed class PricingPolicyGateway(ISettingsPort settings) : IPricingPolicyGateway
{
    public async Task<TimeSpan> GetFxMaxRateAgeAsync(CancellationToken ct = default) =>
        (await settings.GetCommerceLimitsAsync(ct)).FxMaxRateAge;
}
