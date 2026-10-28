namespace EwaysVNext.PaymentTests;

/// <summary>Test-only timer control, so timeout behavior is independent of wall-clock scheduling.</summary>
internal sealed class ManualGatewayTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private readonly DateTimeOffset _startedAt;
    private TimeSpan _elapsed;

    public ManualGatewayTimeProvider(DateTimeOffset? startedAt = null) =>
        _startedAt = startedAt ?? new DateTimeOffset(2026, 9, 29, 12, 30, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _startedAt + _elapsed;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        _elapsed += duration;
        foreach (var timer in _timers.ToArray()) timer.FireIfDue();
    }

    private sealed class ManualTimer(ManualGatewayTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _due = TimeSpan.MaxValue;
        private TimeSpan _period;
        private bool _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) return false;
            _due = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : clock._elapsed + dueTime;
            _period = period;
            return true;
        }
        public void FireIfDue()
        {
            if (_disposed || _due > clock._elapsed) return;
            _due = _period == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : clock._elapsed + _period;
            callback(state);
        }
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
