namespace EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

public sealed record GatewayInquiryResult(
    Money BankAmount,
    string ProviderReference,
    DateTimeOffset? TransactionTime = null,
    string? ProviderStatus = null,
    string? ProviderCode = null,
    string? ProviderMessage = null);
