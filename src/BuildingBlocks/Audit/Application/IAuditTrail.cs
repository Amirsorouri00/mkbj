namespace EwaysVNext.Audit;

/// <summary>
/// Records an admin action.
/// <para>
/// The entry is added to the caller's unit of work and persisted by <b>the caller's own save</b>.
/// That is deliberate: the audit row and the change it describes commit together, so there can be
/// no audited change that did not happen and no change that was not audited.
/// </para>
/// </summary>
public interface IAuditTrail
{
    void Record(
        AuditActor actor,
        string action,
        string targetType,
        string targetId,
        object? before,
        object? after);
}
