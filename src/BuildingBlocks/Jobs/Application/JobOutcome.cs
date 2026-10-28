namespace EwaysVNext.Jobs;

/// <summary>
/// What a handler decided. A handler returns one of these rather than throwing, so "retry in ten
/// minutes because the provider is rate-limiting us" is expressible and not confused with a bug.
/// </summary>
public abstract record JobOutcome
{
    private JobOutcome() { }

    public static JobOutcome Completed { get; } = new CompletedOutcome();

    /// <summary>Try again later. Attempts still count towards the limit.</summary>
    public static JobOutcome RetryLater(TimeSpan delay, string reason) => new RetryLaterOutcome(delay, reason);

    /// <summary>Give up now, without using the remaining attempts.</summary>
    public static JobOutcome Failed(string reason) => new FailedOutcome(reason);

    public sealed record CompletedOutcome : JobOutcome;
    public sealed record RetryLaterOutcome(TimeSpan Delay, string Reason) : JobOutcome;
    public sealed record FailedOutcome(string Reason) : JobOutcome;
}
