namespace EwaysVNext.Jobs.Implementation;

public sealed record ClaimedJob(long Id, string Type, string Payload, int Attempts, int MaxAttempts);

/// <summary>
/// The storage operations behind the worker loop. Every method is a single statement so that
/// several workers can run them concurrently without coordinating.
/// </summary>
public interface IJobStore
{
    /// <summary>
    /// Takes up to <paramref name="batchSize"/> jobs for <paramref name="workerId"/>, skipping rows
    /// another worker already holds. Also reclaims jobs whose lease expired — a worker that died
    /// mid-run must not strand its job.
    /// </summary>
    Task<IReadOnlyList<ClaimedJob>> ClaimAsync(
        string workerId, int batchSize, TimeSpan lease, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Extends the lease. False means the job was reclaimed and this worker must stop.</summary>
    Task<bool> HeartbeatAsync(
        long jobId, string workerId, TimeSpan lease, DateTimeOffset now, CancellationToken ct = default);

    Task CompleteAsync(long jobId, string workerId, DateTimeOffset now, CancellationToken ct = default);

    Task RetryAsync(long jobId, string workerId, DateTimeOffset runAt, string reason, CancellationToken ct = default);

    Task FailAsync(long jobId, string workerId, string reason, DateTimeOffset now, CancellationToken ct = default);

    Task RegisterRecurringAsync(string type, TimeSpan interval, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Returns the types now due, having already advanced their next run.</summary>
    Task<IReadOnlyList<string>> ClaimDueRecurringAsync(DateTimeOffset now, CancellationToken ct = default);
}
