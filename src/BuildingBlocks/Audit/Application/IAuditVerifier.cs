namespace EwaysVNext.Audit;

/// <summary>
/// Checks the hash chain over the audit log. The chain makes tampering <b>detectable</b>; it does
/// not prevent it. Prevention is the database grant (the application role cannot UPDATE or DELETE
/// audit rows), and anyone with direct database access can still rewrite history — but not without
/// breaking the chain.
/// </summary>
public interface IAuditVerifier
{
    Task<AuditVerificationResult> VerifyAsync(CancellationToken ct = default);
}

/// <param name="FirstBrokenEventId">The first event whose hash does not match, if any.</param>
public sealed record AuditVerificationResult(
    bool IsIntact,
    long SealedEvents,
    long UnsealedEvents,
    long? FirstBrokenEventId);
