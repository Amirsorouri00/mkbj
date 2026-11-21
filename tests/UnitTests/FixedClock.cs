using Mkbj.Domain.Common;

namespace Mkbj.UnitTests;

internal sealed class FixedClock(DateTimeOffset? now = null) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now ?? TestTime.Now;

    public DateTimeOffset Advance(TimeSpan by) => UtcNow += by;
}
