using System.Text.Json.Serialization;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat.Contracts;

internal sealed record MellatResponse(
    [property: JsonPropertyName("resCode")] string? ResCode,
    [property: JsonPropertyName("encRefId")] string? EncRefId,
    [property: JsonPropertyName("internalRefrenceId")] long? InternalRefrenceId);
