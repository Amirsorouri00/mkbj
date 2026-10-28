using System.Text.Json.Serialization;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat.Contracts;

internal sealed record MellatInquiryResponse
{
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonPropertyName("responseCode")]
    public string? ResponseCode { get; init; }

    [JsonPropertyName("responseMessage")]
    public string? ResponseMessage { get; init; }

    [JsonPropertyName("transactionInfoList")]
    public List<MellatInquiryRecord>? TransactionInfoList { get; init; }
}

internal sealed record MellatInquiryRecord
{
    [JsonPropertyName("terminalId")]
    public long? TerminalId { get; init; }

    [JsonPropertyName("internalReferenceId")]
    public long? InternalReferenceId { get; init; }

    [JsonPropertyName("transactionDate")]
    public string? TransactionDate { get; init; }

    [JsonPropertyName("transactionTime")]
    public string? TransactionTime { get; init; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; init; }
}
