namespace EwaysVNext.Domain.Common;

internal static class SnowflakeIdGeneratorMessages
{
    public const string ClockBeforeEpochFormat = "The system clock is earlier than the Snowflake epoch ({0:O}); refusing to generate an id.";
    public const string EpochExhausted = "The Snowflake timestamp range is exhausted; ids can no longer be generated.";
}
