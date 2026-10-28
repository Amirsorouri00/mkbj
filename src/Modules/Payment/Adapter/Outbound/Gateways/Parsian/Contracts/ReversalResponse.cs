namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;

internal sealed record ReversalResponse(short? Status, string? Message, long? Token);
