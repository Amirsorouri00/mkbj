using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Sales.Application.Domain.Telemetry;

/// <summary>What one run of the composition engine did.</summary>
public sealed record CompositionAttemptRecord(
    DateTimeOffset At,
    long? PartnerId,
    string Currency,
    long TargetMinor,
    bool Succeeded,
    string? ErrorCode,
    long DurationMs,
    int Lines,
    int Iterations,
    int CandidatePoolSize,
    long Seed,
    string StrategyVersion,
    int Recompositions,
    long? OrderId);

/// <summary>
/// One row per composition run, whether it succeeded or not.
/// </summary>
/// <remarks>
/// <para>
/// A failed composition writes no business row at all — no order, no invoice, nothing. This is
/// the only trace it leaves, and without it "the engine declines too often" would be a feeling
/// rather than a number. The dashboard's success rate, p95 and recent failure codes read this
/// table and nothing else.
/// </para>
/// <para>
/// Append-only, and written in its own scope and save on a best-effort basis: telemetry must
/// never fail or roll back an order. It is not an aggregate, so that separate save does not
/// breach the one-save-per-operation rule.
/// </para>
/// </remarks>
public sealed class CompositionAttempt : Entity<long>
{
    private CompositionAttempt() { }

    public static CompositionAttempt Record(long id, CompositionAttemptRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new CompositionAttempt
        {
            Id = id,
            At = record.At,
            PartnerId = record.PartnerId,
            Currency = record.Currency,
            TargetMinor = record.TargetMinor,
            Succeeded = record.Succeeded,
            ErrorCode = record.ErrorCode,
            DurationMs = record.DurationMs,
            Lines = record.Lines,
            Iterations = record.Iterations,
            CandidatePoolSize = record.CandidatePoolSize,
            Seed = record.Seed,
            StrategyVersion = record.StrategyVersion,
            Recompositions = record.Recompositions,
            OrderId = record.OrderId,
        };
    }

    public DateTimeOffset At { get; private set; }
    public long? PartnerId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public long TargetMinor { get; private set; }
    public bool Succeeded { get; private set; }
    public string? ErrorCode { get; private set; }
    public long DurationMs { get; private set; }
    public int Lines { get; private set; }
    public int Iterations { get; private set; }
    public int CandidatePoolSize { get; private set; }

    /// <summary>Kept even on a failure: a failing seed is what makes the failure reproducible.</summary>
    public long Seed { get; private set; }

    public string StrategyVersion { get; private set; } = string.Empty;

    /// <summary>How many times a price moved under the engine and it had to start again.</summary>
    public int Recompositions { get; private set; }

    public long? OrderId { get; private set; }
}
