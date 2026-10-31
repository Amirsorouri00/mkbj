namespace EwaysVNext.Modules.Partners.Application.Usage;

/// <summary>One call, as handed to the recorder. Ids are assigned when the batch is written.</summary>
public sealed record ApiKeyUsageEntry(
    long PartnerId,
    long KeyId,
    DateTimeOffset At,
    string Method,
    string Path,
    int Status,
    string CorrelationId,
    int DurationMs);
