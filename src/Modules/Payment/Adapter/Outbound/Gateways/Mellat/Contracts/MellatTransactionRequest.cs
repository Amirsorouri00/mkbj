using System.Text.Json.Serialization;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat.Contracts;

internal sealed record MellatTransactionRequest(
    [property: JsonPropertyName("terminalId")] long TerminalId,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("userPassword")] string UserPassword,
    [property: JsonPropertyName("orderId")] long OrderId,
    [property: JsonPropertyName("saleOrderId")] long SaleOrderId,
    [property: JsonPropertyName("saleReferenceId")] long SaleReferenceId);
