namespace EwaysVNext.Jobs;

public interface IJobQueue
{
    /// <summary>
    /// Schedules work for a worker to pick up.
    /// <para>
    /// This is its own statement, never part of the caller's save. Callers enqueue <b>after</b>
    /// their business write has committed: a job enqueued in the same transaction would either be
    /// lost with a rollback or, worse, be claimed by a worker before the row it refers to exists.
    /// </para>
    /// <para>
    /// <paramref name="dedupeKey"/> coalesces: while a job with that key is still queued, another
    /// enqueue is dropped. It does not deduplicate against a job already running, so a refresh
    /// requested while one is in flight still gets its own run.
    /// </para>
    /// </summary>
    Task EnqueueAsync<TPayload>(
        string type,
        TPayload payload,
        DateTimeOffset? runAt = null,
        string? dedupeKey = null,
        CancellationToken ct = default);
}
