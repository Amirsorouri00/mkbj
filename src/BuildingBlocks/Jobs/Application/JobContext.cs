namespace EwaysVNext.Jobs;

/// <summary>
/// What a handler knows about the run it is in.
/// </summary>
/// <param name="Attempt">1 on the first run. A handler can use it to widen a batch or give up early.</param>
/// <param name="HeartbeatAsync">
/// Extends the lease. A handler doing work longer than the lease must call this, or another worker
/// will reclaim the job and run it concurrently — which is why every handler must also be
/// idempotent: leases expire, processes die, and at-least-once is the guarantee on offer.
/// </param>
public sealed record JobContext(long JobId, int Attempt, Func<CancellationToken, Task> HeartbeatAsync);
