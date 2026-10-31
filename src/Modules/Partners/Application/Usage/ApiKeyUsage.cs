namespace EwaysVNext.Modules.Partners.Application.Usage;

/// <summary>
/// One recorded partner API call (<c>partners.api_key_usage</c>).
/// <para>
/// Not an aggregate and deliberately not part of <c>Partner</c>: these rows are append-only
/// facts, written in batches by a background flush, and nothing about a partner's invariants
/// depends on them. Putting them inside the aggregate would mean loading a partner's entire
/// request history to change its name.
/// </para>
/// <para>
/// It carries no request or response body, no headers and no query string — only what answers
/// "who called what, when, and how did it end".
/// </para>
/// </summary>
public sealed class ApiKeyUsage
{
    public long Id { get; set; }

    public long PartnerId { get; set; }

    public long KeyId { get; set; }

    public DateTimeOffset At { get; set; }

    public string Method { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public int Status { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public int DurationMs { get; set; }
}
