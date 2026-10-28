namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;

internal sealed record ConfirmPaymentRequest(string LoginAccount, long Token);
