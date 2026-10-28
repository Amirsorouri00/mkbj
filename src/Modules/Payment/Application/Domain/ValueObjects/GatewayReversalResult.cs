namespace EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

public enum GatewayReversalState { Reversed, StillConfirmed, Unknown }

public sealed record GatewayReversalResult(
    GatewayReversalState State,
    string? Reference = null,
    string? ProviderCode = null,
    string? ProviderMessage = null);
