using System.Text.Json.Serialization;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat.Contracts;

internal sealed record MellatSaleRequest(
    [property: JsonPropertyName("terminalId")] long TerminalId,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("userPassword")] string UserPassword,
    [property: JsonPropertyName("orderId")] long OrderId,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("localDate")] string LocalDate,
    [property: JsonPropertyName("localTime")] string LocalTime,
    [property: JsonPropertyName("additionalData")] string AdditionalData,
    [property: JsonPropertyName("callBackUrl")] string CallBackUrl,
    [property: JsonPropertyName("payerId")] string PayerId)
{
    /// <summary>Supported by Mellat, but not supplied by Payment yet.</summary>
    [JsonPropertyName("mobileNo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MobileNo { get; init; }

    /// <summary>Supported by Mellat, but not supplied by Payment yet.</summary>
    [JsonPropertyName("encPan"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EncPan { get; init; }

    /// <summary>Supported by Mellat, but not supplied by Payment yet.</summary>
    [JsonPropertyName("panHiddenMode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PanHiddenMode { get; init; }

    /// <summary>Supported by Mellat, but not supplied by Payment yet.</summary>
    [JsonPropertyName("cartItem"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? CartItem { get; init; }

    /// <summary>Supported by Mellat, but not supplied by Payment yet.</summary>
    [JsonPropertyName("enc"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Enc { get; init; }
}
