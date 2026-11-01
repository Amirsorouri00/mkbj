namespace EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;

/// <summary>
/// The pricing policy the seller set, which lives in Settings. A gateway rather than a direct
/// reference: this is the hexagon, and reaching into another module from here would make the
/// dependency structural instead of a port.
/// </summary>
public interface IPricingPolicyGateway
{
    /// <summary>How old a provider rate may be and still be quoted from.</summary>
    Task<TimeSpan> GetFxMaxRateAgeAsync(CancellationToken ct = default);
}
