using System.Text.Json.Serialization;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat.Contracts;

internal sealed record MellatInquiryRequest(
    [property: JsonPropertyName("referenceId")] long ReferenceId);
