namespace EwaysVNext.Jobs.Implementation;

/// <summary>
/// Exponential backoff between retries: a failing dependency should be given progressively more
/// room rather than hammered every poll interval, and the cap keeps a long-broken job retrying at
/// a sane cadence instead of drifting to never.
/// </summary>
public static class JobBackoff
{
    public static TimeSpan For(int attempt, TimeSpan baseDelay, TimeSpan max)
    {
        if (attempt <= 1) return baseDelay <= max ? baseDelay : max;

        // Shift rather than Math.Pow: a large attempt count would overflow a double-based
        // multiplication long before it reaches the cap.
        var doublings = Math.Min(attempt - 1, 62);
        var ticks = baseDelay.Ticks;

        for (var i = 0; i < doublings; i++)
        {
            if (ticks >= max.Ticks) return max;
            ticks *= 2;
        }

        return ticks >= max.Ticks ? max : TimeSpan.FromTicks(ticks);
    }
}
