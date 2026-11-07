using EwaysVNext.Audit;
using EwaysVNext.Modules.Admin.Application.Ports.Outbound;
using EwaysVNext.Modules.Admin.Application.Views;

namespace EwaysVNext.Modules.Admin.Adapter.Outbound.Gateways;

/// <summary>
/// The audit chain's verdict, stamped with when it was reached.
/// </summary>
/// <remarks>
/// The timestamp is the gateway's addition and it matters: "the log is intact" is a statement
/// about a moment, and one shown without a time is one nobody can tell is yesterday's.
/// </remarks>
internal sealed class AuditReadGateway(IAuditVerifier verifier) : IAuditReadPort
{
    public async Task<AuditVerificationView> VerifyAsync(
        DateTimeOffset now, CancellationToken ct = default)
    {
        var result = await verifier.VerifyAsync(ct);

        return new AuditVerificationView(
            result.Intact,
            result.SealedCount,
            result.FirstMismatchEventId,
            result.UnsealedBelowWatermark,
            now);
    }
}
