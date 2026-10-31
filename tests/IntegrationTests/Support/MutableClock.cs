using EwaysVNext.Domain.Common;

namespace EwaysVNext.IntegrationTests.Support;

/// <summary>
/// Lets a test move time instead of waiting for it. Key expiry and rotation overlap are measured
/// in hours, which no test can afford to spend.
/// </summary>
public sealed class MutableClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}
