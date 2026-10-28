using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

public sealed record GatewayCallbackRequest(
    GatewayPaymentContext Payment, IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// A provider's verdict on a callback: its documented checks and, when the callback reports
/// payment, the bank's server-side confirmation. Trusted context IDs are echoed back for
/// correlation; they are not independent identity evidence.
/// </summary>
public sealed record GatewayCallbackResult(
    long RequestId, string Token, string? ProviderReference,
    GatewayPaymentState State, string? ProviderCode = null);
