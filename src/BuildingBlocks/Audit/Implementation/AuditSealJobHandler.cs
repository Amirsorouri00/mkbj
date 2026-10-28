using EwaysVNext.Domain.Common;
using EwaysVNext.Jobs;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Audit.Implementation;

/// <summary>
/// Extends the hash chain over events that have settled.
/// <para>
/// It deliberately ignores anything younger than <see cref="SettlingPeriod"/>. Ids are Snowflake
/// values assigned before the insert commits, so a slow transaction can commit a lower id after a
/// faster one — sealing right to the newest row would chain over an event, then have an earlier id
/// appear beneath it and read as a gap forever.
/// </para>
/// </summary>
public sealed class AuditSealJobHandler(DbContext db, IClock clock) : IJobHandler<EmptyPayload>
{
    /// <summary>How long an event must have existed before it is sealed.</summary>
    public static readonly TimeSpan SettlingPeriod = TimeSpan.FromSeconds(60);

    private const int BatchSize = 1000;

    public string Type => JobTypes.AuditSeal;

    public async Task<JobOutcome> HandleAsync(
        EmptyPayload payload, JobContext context, CancellationToken ct = default)
    {
        var lastSeal = await db.Set<AuditSeal>()
            .AsNoTracking().OrderByDescending(seal => seal.EventId).FirstOrDefaultAsync(ct);

        var watermark = lastSeal?.EventId ?? long.MinValue;
        var cutoff = clock.UtcNow - SettlingPeriod;

        // Read the stored values back rather than hashing what was written: jsonb normalises text,
        // so a hash computed from the in-memory string would not match on verification.
        var rows = await db.Set<AuditEvent>().AsNoTracking()
            .Where(e => e.Id > watermark && e.OccurredAt <= cutoff)
            .OrderBy(e => e.Id)
            .Take(BatchSize)
            .Select(e => new AuditEventRow(
                e.Id, e.OccurredAt, e.ActorUserId, e.ActorRole, e.Action,
                e.TargetType, e.TargetId, e.Before, e.After, e.CorrelationId))
            .ToListAsync(ct);

        if (rows.Count == 0) return JobOutcome.Completed;

        var previous = lastSeal?.Hash ?? AuditHashing.Genesis;
        var sealedAt = clock.UtcNow;

        foreach (var row in rows)
        {
            previous = AuditHashing.Next(previous, row);
            db.Add(new AuditSeal { EventId = row.Id, Hash = previous, SealedAt = sealedAt });
        }

        // One save for a batch of independent, append-only rows: no invariant spans two seals
        // (AGENTS.md rule 7).
        await db.SaveChangesAsync(ct);

        return JobOutcome.Completed;
    }
}
