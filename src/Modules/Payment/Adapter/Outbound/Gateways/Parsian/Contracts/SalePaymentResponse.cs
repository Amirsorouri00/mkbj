namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;

internal sealed record SalePaymentResponse(short? Status, string? Message, long? Token);
