using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Sales.Application.Services;

/// <summary>
/// The administrator's three acts on an order: confirm, cancel, refund.
/// </summary>
/// <remarks>
/// <para>
/// Each is load → allocate any number → call the aggregate → record the audit → <b>one</b> save.
/// The audit goes in before the save, not after, so it commits with the change: an order that
/// moved with no record of who moved it is worse than one that did not move.
/// </para>
/// <para>
/// What the audit carries is the status dimensions and the refunded total, and nothing else. An
/// audit log is read by people who have no business seeing a buyer's address, and a trail that
/// quietly accumulated personal data would be the last place anyone thought to look for it.
/// </para>
/// </remarks>
internal sealed class OrderAdminService(
    IOrderRepository orders,
    IDocumentNumberAllocator numbers,
    ISalesSettingsGateway settings,
    ITaxPolicy taxPolicy,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : IOrderAdminService
{
    private const string TargetType = "Order";

    public async Task<Result> ConfirmExternalPaymentAsync(
        long orderId, string method, string? reference, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(method))
            return Result.Failure(SalesErrors.ValidationFailed("A payment method is required."));

        var order = await orders.GetByIdAsync(orderId, ct);
        if (order is null) return Result.Failure(SalesErrors.NotFound);

        var before = Snapshot(order);

        var (seller, requiredBuyerFields, _) = await settings.GetSellerAsync(ct);
        var invoiceNumber = await numbers.NextAsync(DocumentNumberKind.Commercial, ct);

        try
        {
            order.ConfirmExternalPayment(
                ids.Generate(),
                new IssuedDocumentRef(ids.Generate(), invoiceNumber),
                actor.UserId,
                method,
                reference,
                seller,
                requiredBuyerFields,
                taxPolicy.Assess(order.SubtotalMinor, order.Currency),
                clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(FromDomain(exception));
        }

        audit.Record(
            actor.ToAudit(), "sales.order.confirm_payment", TargetType, Key(orderId),
            before, Snapshot(order));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> CancelAsync(
        long orderId, AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var order = await orders.GetByIdAsync(orderId, ct);
        if (order is null) return Result.Failure(SalesErrors.NotFound);

        var before = Snapshot(order);

        try
        {
            // No number is allocated: a cancellation issues no document, and drawing one would
            // leave a gap in a sequence an auditor reads as a missing invoice.
            order.Cancel(clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(FromDomain(exception));
        }

        audit.Record(
            actor.ToAudit(), "sales.order.cancel", TargetType, Key(orderId),
            before, Snapshot(order));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<RefundResult>> RecordRefundAsync(
        long orderId, long amountMinor, string reason, string? externalReference,
        AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Before the order is even loaded. A staff account must not be able to tell from the
        // response whether an order exists, let alone what it is worth.
        if (!actor.IsSuperAdmin) return Result.Failure<RefundResult>(Error.Forbidden);

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<RefundResult>(SalesErrors.ValidationFailed("A reason is required."));

        var order = await orders.GetByIdAsync(orderId, ct);
        if (order is null) return Result.Failure<RefundResult>(SalesErrors.NotFound);

        var before = Snapshot(order);

        var (seller, requiredBuyerFields, _) = await settings.GetSellerAsync(ct);
        var creditNoteNumber = await numbers.NextAsync(DocumentNumberKind.CreditNote, ct);
        var creditNote = new IssuedDocumentRef(ids.Generate(), creditNoteNumber);

        Refund refund;
        try
        {
            refund = order.RecordRefund(
                ids.Generate(), creditNote, amountMinor, reason, actor.UserId, externalReference,
                seller, requiredBuyerFields, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure<RefundResult>(FromDomain(exception));
        }

        audit.Record(
            actor.ToAudit(), "sales.order.refund", TargetType, Key(orderId),
            before, Snapshot(order));

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new RefundResult(
            refund.Id,
            creditNote.DocumentId,
            creditNote.Number,
            order.PaymentStatus.ToString(),
            order.RefundedMinor));
    }

    /// <summary>
    /// What changed, in the terms an auditor asks about. No buyer, no lines, no prices.
    /// </summary>
    private static object Snapshot(Order order) => new
    {
        order.Status,
        order.PaymentStatus,
        order.PaymentSource,
        order.FulfillmentStatus,
        order.DocumentStatus,
        order.RefundedMinor,
    };

    private static string Key(long orderId) =>
        orderId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The aggregate's refusals, as errors the caller can act on.
    /// </summary>
    /// <remarks>
    /// Each keeps its own code and status. Flattening them to one would tell an administrator
    /// that "something was wrong" when the useful answer is either "this order is not in that
    /// state" or "only this much is left to refund".
    /// </remarks>
    private static Error FromDomain(DomainException exception) => exception.Code switch
    {
        "ORDER_STATE_CONFLICT" => SalesErrors.OrderStateConflict,
        "REFUND_EXCEEDS_REFUNDABLE" => new Error(
            SalesErrors.RefundExceedsRefundable.Code, exception.Message, 422),
        "INVOICE_BUYER_FIELDS_MISSING" => SalesErrors.InvoiceBuyerFieldsMissing,
        "ORDER_NOT_INVOICED" => SalesErrors.OrderStateConflict,
        _ => SalesErrors.ValidationFailed(exception.Message),
    };
}
