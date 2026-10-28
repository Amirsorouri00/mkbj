using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EwaysVNext.Audit.Implementation;

/// <summary>One audit row, as the chain sees it.</summary>
public sealed record AuditEventRow(
    long Id,
    DateTimeOffset OccurredAt,
    Guid ActorUserId,
    string ActorRole,
    string Action,
    string TargetType,
    string TargetId,
    string? Before,
    string? After,
    string CorrelationId);

/// <summary>
/// The hash chain: each link covers the previous hash and the full content of one event, so
/// changing any field of any sealed event breaks every link after it.
/// </summary>
public static class AuditHashing
{
    /// <summary>What the first link chains from.</summary>
    public static byte[] Genesis { get; } = new byte[32];

    /// <summary>
    /// Fields are joined with US (unit separator, U+001F) — a control character that cannot appear
    /// in any of them, so no combination of values can be rearranged to produce the same input.
    /// </summary>
    private const char Separator = '\u001f';

    public static byte[] Next(ReadOnlySpan<byte> previous, AuditEventRow row)
    {
        var canonical = string.Join(Separator,
            row.Id.ToString(CultureInfo.InvariantCulture),
            row.OccurredAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            row.ActorUserId.ToString("D", CultureInfo.InvariantCulture),
            row.ActorRole,
            row.Action,
            row.TargetType,
            row.TargetId,
            row.Before ?? string.Empty,
            row.After ?? string.Empty,
            row.CorrelationId);

        var content = Encoding.UTF8.GetBytes(canonical);
        var buffer = new byte[previous.Length + content.Length];
        previous.CopyTo(buffer);
        content.CopyTo(buffer, previous.Length);

        return SHA256.HashData(buffer);
    }
}
