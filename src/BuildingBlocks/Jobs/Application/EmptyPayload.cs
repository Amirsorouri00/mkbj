namespace EwaysVNext.Jobs;

/// <summary>For jobs that carry no data — a recurring refresh, a sweep.</summary>
public sealed record EmptyPayload
{
    public static EmptyPayload Instance { get; } = new();
}
