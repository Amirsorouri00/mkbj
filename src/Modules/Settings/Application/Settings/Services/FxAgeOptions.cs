namespace EwaysVNext.Modules.Settings.Application.Settings.Services;

/// <summary>
/// How stale an FX rate may be before the system refuses to price with it.
/// <para>
/// Configuration rather than a stored setting, and read from the same <c>Fx</c> section the
/// pricing side reads. Two copies of this number that could disagree would mean one component
/// accepting a rate another has already rejected.
/// </para>
/// </summary>
public sealed class FxAgeOptions
{
    public const string SectionName = "Fx";

    public TimeSpan MaxRateAge { get; init; } = TimeSpan.FromHours(72);
}
