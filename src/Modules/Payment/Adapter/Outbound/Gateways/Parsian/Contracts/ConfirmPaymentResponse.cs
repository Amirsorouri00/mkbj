namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;

internal sealed record ConfirmPaymentResponse(short? Status, string? CardNumberMasked, long? Token, long? RRN);
