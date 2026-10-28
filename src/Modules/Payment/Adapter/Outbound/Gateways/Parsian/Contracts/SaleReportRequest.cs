using System.Text.Json.Serialization;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;

internal sealed record SaleReportRequest(
    [property: JsonPropertyName("RRN")] long RRN,
    [property: JsonPropertyName("OrderId")] long OrderId,
    [property: JsonPropertyName("Token")] long Token,
    [property: JsonPropertyName("FromDate")] DateTime FromDate,
    [property: JsonPropertyName("ToDate")] DateTime ToDate);
