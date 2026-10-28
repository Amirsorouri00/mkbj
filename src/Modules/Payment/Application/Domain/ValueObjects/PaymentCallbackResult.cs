namespace EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

/// <summary>
/// The BankTransaction lifecycle must retain <see cref="Attempt"/>. A duplicate callback for an
/// already confirmed attempt returns the stored outcome without contacting the bank.
/// </summary>
public sealed record PaymentCallbackResult(
    GatewayAttempt Attempt, GatewayPaymentState State, string? ProviderCode, bool IsDuplicate);
