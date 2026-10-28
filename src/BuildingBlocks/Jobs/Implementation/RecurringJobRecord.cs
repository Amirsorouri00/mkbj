namespace EwaysVNext.Jobs.Implementation;

/// <summary>
/// A job that should be enqueued on a fixed interval. <see cref="NextRunAt"/> is advanced by the
/// same guarded UPDATE that reads it, so several workers ticking at once still produce one run.
/// </summary>
public sealed class RecurringJobRecord
{
    public string Type { get; set; } = string.Empty;
    public TimeSpan Interval { get; set; }
    public DateTimeOffset NextRunAt { get; set; }
}
