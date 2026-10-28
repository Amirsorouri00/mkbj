namespace EwaysVNext.Audit.Implementation;

/// <summary>
/// The hash-chain link for one audit event: <c>sha256(previous_hash || this row)</c>.
/// <para>
/// Kept in its own table rather than as a column on the event, so the application role needs no
/// UPDATE permission on <c>audit_events</c> to seal it — a role that could update the event row to
/// write a hash could also update the rest of it.
/// </para>
/// </summary>
public sealed class AuditSeal
{
    public long EventId { get; set; }
    public byte[] Hash { get; set; } = [];
    public DateTimeOffset SealedAt { get; set; }
}
