using Mkbj.Modules.Pricing.Application.Prices.Ports.Outbound;
using Mkbj.Modules.Settings.Application.Settings.Ports.Inbound;

namespace Mkbj.Modules.Pricing.Adapter.Outbound.Gateways;

/// <summary>
/// Anti-corruption layer: Pricing learns one duration from Settings, not Settings' shape.
/// </summary>
internal sealed class PricingPolicyGateway(ISettingsPort settings) : IPricingPolicyGateway
{
    public async Task<TimeSpan> GetFxMaxRateAgeAsync(CancellationToken ct = default) =>
        (await settings.GetCommerceLimitsAsync(ct)).FxMaxRateAge;
}
