using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

public sealed record GatewayInquiryObservation(
    Money BankAmount,
    string ProviderReference,
    DateTimeOffset? TransactionTime = null,
    string? ProviderStatus = null,
    string? ProviderCode = null,
    string? ProviderMessage = null);
