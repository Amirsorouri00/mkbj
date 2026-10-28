using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

/// <summary>
/// Trusted attempt context built by the high-level gateway from the BankTransaction and
/// its retained attempt. Never reconstruct it from browser callback fields.
/// </summary>
public sealed record GatewayPaymentContext(long BankTransactionId, Money Amount, GatewayAttempt Attempt)
{
    public string ProviderName => Attempt.ProviderName;
    public long RequestId => Attempt.RequestId;
    public string Token => Attempt.Token;
    public string? ProviderReference => Attempt.ProviderReference;
}
