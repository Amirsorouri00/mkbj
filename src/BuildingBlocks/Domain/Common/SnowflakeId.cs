using System.Globalization;

namespace EwaysVNext.Domain.Common;

public readonly struct SnowflakeId(long value) : IEquatable<SnowflakeId>
{
    public long Value { get; } = value;

    public static implicit operator SnowflakeId(long value) => new(value);
    public static implicit operator long(SnowflakeId id) => id.Value;

    public bool Equals(SnowflakeId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is SnowflakeId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public static bool operator ==(SnowflakeId left, SnowflakeId right) => left.Equals(right);
    public static bool operator !=(SnowflakeId left, SnowflakeId right) => !left.Equals(right);
}
