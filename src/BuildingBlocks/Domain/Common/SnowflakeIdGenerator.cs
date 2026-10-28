using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EwaysVNext.Domain.Common;

public sealed class SnowflakeIdGenerator(IClock? clock = null) : IIdGenerator
{
    public const int TimestampBits = 41;
    public const int SequenceBits = 22;
    public const long MaxTimestamp = (1L << TimestampBits) - 1;
    public const long MaxSequence = (1L << SequenceBits) - 1;
    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly CompositeFormat ClockBeforeEpochFormat =
        CompositeFormat.Parse(SnowflakeIdGeneratorMessages.ClockBeforeEpochFormat);

    private readonly IClock _clock = clock ?? new SystemClock();
    private readonly Lock _lock = new();
    private long _lastTimestamp = -1;
    private long _sequence;

    public long Generate()
    {
        var now = MillisecondsSinceEpoch();

        lock (_lock)
        {
            if (now > _lastTimestamp)
            {
                _lastTimestamp = now;
                _sequence = RandomSequenceStart();
            }
            else if (_sequence < MaxSequence)
            {
                _sequence++;
            }
            else
            {
                _lastTimestamp++;
                _sequence = RandomSequenceStart();
            }

            if (_lastTimestamp > MaxTimestamp)
                throw new InvalidOperationException(SnowflakeIdGeneratorMessages.EpochExhausted);

            return (_lastTimestamp << SequenceBits) | _sequence;
        }
    }

    public static DateTimeOffset GetCreatedAt(long id) => Epoch.AddMilliseconds(id >> SequenceBits);

    private long MillisecondsSinceEpoch()
    {
        var milliseconds = (long)(_clock.UtcNow - Epoch).TotalMilliseconds;
        if (milliseconds < 0)
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, ClockBeforeEpochFormat, Epoch));
        return milliseconds;
    }

    private static long RandomSequenceStart() => RandomNumberGenerator.GetInt32((int)(MaxSequence + 1));
}
