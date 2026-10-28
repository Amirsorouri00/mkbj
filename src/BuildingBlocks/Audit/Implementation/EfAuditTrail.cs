using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Audit.Implementation;

/// <summary>
/// Adds the audit entry to the caller's change tracker and <b>never saves</b>. The caller's own
/// <c>SaveChangesAsync</c> commits the entry together with the change it describes, so the two
/// cannot diverge.
/// <para>
/// An append-only row is not an aggregate with invariants, so writing it alongside another
/// aggregate does not breach AGENTS.md rule 7.
/// </para>
/// </summary>
public sealed class EfAuditTrail(
    DbContext db,
    IIdGenerator ids,
    IClock clock,
    ICorrelationContext correlation) : IAuditTrail
{
    public void Record(
        AuditActor actor, string action, string targetType, string targetId, object? before, object? after)
    {
        var (changedBefore, changedAfter) = AuditDiff.Changed(before, after);

        db.Add(new AuditEvent
        {
            Id = ids.Generate(),
            OccurredAt = clock.UtcNow,
            ActorUserId = actor.UserId,
            ActorRole = actor.Role,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Before = changedBefore?.ToJsonString(),
            After = changedAfter?.ToJsonString(),
            CorrelationId = correlation.CorrelationId,
        });
    }
}
