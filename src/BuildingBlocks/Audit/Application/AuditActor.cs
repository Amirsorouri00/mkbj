namespace EwaysVNext.Audit;

/// <summary>
/// Who performed an audited action. <paramref name="Role"/> is recorded alongside the id because
/// a user's role can change after the fact; the audit row must say what they were at the time.
/// </summary>
public sealed record AuditActor(Guid UserId, string Role)
{
    /// <summary>The platform itself — a job, a migration, a startup seed. Never a person.</summary>
    public static AuditActor System { get; } = new(Guid.Empty, "system");
}
