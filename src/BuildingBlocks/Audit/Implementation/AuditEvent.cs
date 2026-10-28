namespace EwaysVNext.Audit.Implementation;

/// <summary>
/// One recorded admin action. A plain class, not <c>Entity&lt;TId&gt;</c>: an audit row is never
/// updated, so a concurrency token would be meaningless, and it raises no domain events.
/// </summary>
public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public Guid ActorUserId { get; set; }

    /// <summary>The actor's role at the time. Roles change; the record of what they were must not.</summary>
    public string ActorRole { get; set; } = string.Empty;

    /// <summary>Dotted and past tense: "price.changed", "partner.suspended".</summary>
    public string Action { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;

    /// <summary>Only the fields that changed — see <see cref="AuditDiff"/>.</summary>
    public string? Before { get; set; }
    public string? After { get; set; }

    public string CorrelationId { get; set; } = string.Empty;
}
