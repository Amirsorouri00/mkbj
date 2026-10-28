namespace EwaysVNext.Jobs.Implementation;

/// <summary>
/// A row in <c>jobs.job</c>. Claiming, completing and retrying all happen in raw SQL so they are
/// single atomic statements; this type exists for the EF model, the migration and reads.
/// </summary>
public sealed class JobRecord
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public JobStatus Status { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }

    /// <summary>The worker holding the lease, and until when. Null while queued.</summary>
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }

    public string? LastError { get; set; }

    /// <summary>Unique among queued rows only — see <see cref="IJobQueue.EnqueueAsync"/>.</summary>
    public string? DedupeKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
