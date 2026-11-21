namespace Mkbj.Modules.Sales.Application.Domain.Telemetry;

/// <summary>A durable receipt for an authenticated order submission, including failed attempts.</summary>
public sealed class PartnerOrderRequest
{
    public long Id { get; set; }
    public long PartnerId { get; set; }
    public long KeyId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string RequestBody { get; set; } = string.Empty;
    public string[] ExternalOrderIds { get; set; } = [];
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public bool Replayed { get; set; }
}
