using EwaysVNext.Core.Application;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Ports.Inbound;

public sealed record RefundResult(
    long RefundId,
    long CreditNoteDocumentId,
    string CreditNoteNumber,
    string PaymentStatus,
    long RefundedMinor);

/// <summary>
/// What an administrator may do to an order.
/// </summary>
/// <remarks>
/// Three acts, all of them irreversible and all of them audited. The audit entry is written in
/// the same save as the change, so there is no window in which an order moved and no record says
/// who moved it.
/// </remarks>
public interface IOrderAdminService
{
    Task<Result> ConfirmExternalPaymentAsync(
        long orderId, string method, string? reference, AdminActor actor,
        CancellationToken ct = default);

    Task<Result> CancelAsync(long orderId, AdminActor actor, CancellationToken ct = default);

    /// <summary>
    /// Sends money back.
    /// </summary>
    /// <remarks>
    /// Super admin only, checked here rather than only at the endpoint. The route policy is the
    /// first gate and this is the second: a refund is the one operation that moves money out of
    /// the business, and it must not become reachable because somebody edited a permission row
    /// or mapped a new route without thinking.
    /// </remarks>
    Task<Result<RefundResult>> RecordRefundAsync(
        long orderId, long amountMinor, string reason, string? externalReference,
        AdminActor actor, CancellationToken ct = default);
}
