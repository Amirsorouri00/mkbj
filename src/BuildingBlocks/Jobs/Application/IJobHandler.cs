namespace EwaysVNext.Jobs;

/// <summary>
/// Handles one job type. Implementations must be <b>idempotent</b>: a lease can expire mid-run and
/// the job will be claimed again, so the same payload can be processed more than once.
/// </summary>
public interface IJobHandler<in TPayload>
{
    /// <summary>One of <see cref="JobTypes"/>.</summary>
    string Type { get; }

    Task<JobOutcome> HandleAsync(TPayload payload, JobContext context, CancellationToken ct = default);
}
