namespace EwaysVNext.Modules.Partners.Application.Partners.Keys;

/// <summary>
/// How long a rotated key keeps working.
/// <para>
/// Rotation without an overlap is an outage: the partner's old key stops the instant the new one
/// is issued, before anyone has deployed it. The overlap is the window in which both work. The
/// maximum exists because an overlap is, for its duration, a second live credential — "rotated"
/// with a year's grace has not been rotated.
/// </para>
/// </summary>
public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";

    public TimeSpan RotationOverlap { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan MaxRotationOverlap { get; init; } = TimeSpan.FromDays(30);
}
