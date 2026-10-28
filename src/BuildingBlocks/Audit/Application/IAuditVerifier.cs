namespace EwaysVNext.Audit;

/// <summary>
/// Checks the hash chain over the audit log. The chain makes tampering <b>detectable</b>; it does
/// not prevent it. Prevention is the database grant (the application role cannot UPDATE or DELETE
/// audit rows), and anyone with direct database access can still rewrite history — but not without
/// breaking the chain or leaving a gap.
/// </summary>
public interface IAuditVerifier
{
    Task<AuditVerificationResult> VerifyAsync(CancellationToken ct = default);
}

/// <param name="Intact">True when no hash mismatched and nothing below the watermark is unsealed.</param>
/// <param name="SealedCount">How many events the chain covers.</param>
/// <param name="UnsealedBelowWatermark">
/// Events older than the newest seal that carry no seal of their own. Normally zero: sealing works
/// forward in id order, so a gap below the watermark means a row or a seal was removed.
/// </param>
/// <param name="FirstMismatchEventId">The first sealed event whose recomputed hash differs.</param>
public sealed record AuditVerificationResult(
    bool Intact,
    long SealedCount,
    long UnsealedBelowWatermark,
    long? FirstMismatchEventId);
