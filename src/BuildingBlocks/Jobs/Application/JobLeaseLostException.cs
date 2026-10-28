namespace EwaysVNext.Jobs;

/// <summary>
/// Thrown when a heartbeat finds the job no longer belongs to this worker — the lease expired and
/// someone else claimed it. The handler should stop: continuing would duplicate work another
/// worker is already doing.
/// </summary>
public sealed class JobLeaseLostException(long jobId)
    : Exception($"The lease on job {jobId} was lost; another worker has claimed it.")
{
    public long JobId { get; } = jobId;
}
