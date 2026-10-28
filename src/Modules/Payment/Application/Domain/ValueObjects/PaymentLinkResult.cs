namespace EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

/// <summary>The BankTransaction lifecycle must retain <see cref="Attempt"/> for callback and follow-up calls.</summary>
public sealed record PaymentLinkResult(GatewayAttempt Attempt, GatewayRedirect Redirect);
