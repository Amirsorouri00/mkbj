namespace EwaysVNext.Jobs.Implementation;

public enum JobsMode
{
    /// <summary>The API process also runs the worker loop. The default, and fine for one replica.</summary>
    InProcess,

    /// <summary>This process is a dedicated worker — the shape production compose uses.</summary>
    WorkerOnly,

    /// <summary>No worker. Jobs still enqueue; nothing drains them. Used by the test fixture.</summary>
    Disabled,
}

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    public JobsMode Mode { get; set; } = JobsMode.InProcess;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int BatchSize { get; set; } = 4;

    /// <summary>
    /// How long a claim is held without a heartbeat. Long enough that a slow job is not stolen,
    /// short enough that a crashed worker's job is picked up again promptly.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 10;
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Identifies the lease holder. Must differ per process, or two workers share leases.</summary>
    public string WorkerId { get; set; } = $"{Environment.MachineName}:{Environment.ProcessId}";
}
