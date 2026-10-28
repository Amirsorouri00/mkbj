namespace EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

/// <summary>
/// Callback observations, not persisted aggregate lifecycle states. Confirmed means the bank
/// confirmed the payment server-side; it claims nothing about settlement of funds.
/// </summary>
public enum GatewayPaymentState { Confirmed, Failed, Reversed, Unknown }
