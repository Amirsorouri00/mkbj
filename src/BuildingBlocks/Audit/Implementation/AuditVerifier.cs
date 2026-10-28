using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Audit.Implementation;

/// <summary>
/// Recomputes the chain and reports the first link that does not match, plus any sealed-era event
/// that has lost its seal. Both are how deletion shows up: changing a row breaks its hash, and
/// removing a row or its seal leaves a gap below the watermark.
/// </summary>
public sealed class AuditVerifier(DbContext db) : IAuditVerifier
{
    private const int PageSize = 1000;

    public async Task<AuditVerificationResult> VerifyAsync(CancellationToken ct = default)
    {
        var watermark = await db.Set<AuditSeal>().AsNoTracking()
            .OrderByDescending(seal => seal.EventId)
            .Select(seal => (long?)seal.EventId)
            .FirstOrDefaultAsync(ct);

        if (watermark is null)
            return new AuditVerificationResult(Intact: true, 0, 0, null);

        var previous = AuditHashing.Genesis;
        long sealedCount = 0;
        long? firstMismatch = null;
        long cursor = long.MinValue;

        while (true)
        {
            // Paged and streamed: an audit log is append-only and unbounded, so it must never be
            // loaded whole to be checked.
            var page = await db.Set<AuditSeal>().AsNoTracking()
                .Where(seal => seal.EventId > cursor)
                .OrderBy(seal => seal.EventId)
                .Take(PageSize)
                .Join(db.Set<AuditEvent>().AsNoTracking(),
                    seal => seal.EventId,
                    e => e.Id,
                    (seal, e) => new
                    {
                        seal.Hash,
                        Row = new AuditEventRow(
                            e.Id, e.OccurredAt, e.ActorUserId, e.ActorRole, e.Action,
                            e.TargetType, e.TargetId, e.Before, e.After, e.CorrelationId),
                    })
                .ToListAsync(ct);

            if (page.Count == 0) break;

            foreach (var entry in page)
            {
                previous = AuditHashing.Next(previous, entry.Row);
                sealedCount++;

                if (firstMismatch is null && !previous.AsSpan().SequenceEqual(entry.Hash))
                    firstMismatch = entry.Row.Id;

                cursor = entry.Row.Id;
            }
        }

        // Everything at or below the newest seal should itself be sealed; sealing only moves
        // forward in id order.
        var unsealedBelowWatermark = await db.Set<AuditEvent>().AsNoTracking()
            .Where(e => e.Id <= watermark)
            .CountAsync(e => !db.Set<AuditSeal>().Any(seal => seal.EventId == e.Id), ct);

        return new AuditVerificationResult(
            Intact: firstMismatch is null && unsealedBelowWatermark == 0,
            sealedCount,
            unsealedBelowWatermark,
            firstMismatch);
    }
}
