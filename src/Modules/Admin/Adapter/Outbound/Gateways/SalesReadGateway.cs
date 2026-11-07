using System.Globalization;
using EwaysVNext.Modules.Admin.Application.Ports.Outbound;
using EwaysVNext.Modules.Admin.Application.Views;
using EwaysVNext.Modules.Sales.QueryApplication.Admin;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Admin.Adapter.Outbound.Gateways;

/// <summary>
/// Sales' admin read side, mapped into Admin's own views.
/// </summary>
/// <remarks>
/// Field by field, by hand, and deliberately tedious. The alternative — handing Sales' DTOs
/// straight to the backoffice schema — makes every rename in Sales a breaking change for whatever
/// is drawing the dashboard, and makes the schema impossible to read without opening another
/// module.
/// </remarks>
internal sealed class SalesReadGateway(ISalesAdminQueries sales) : ISalesReadPort
{
    public async Task<SalesSnapshot> GetDashboardAsync(
        DateTimeOffset now, CancellationToken ct = default)
    {
        var dashboard = await sales.GetDashboardAsync(now, ct);

        return new SalesSnapshot(
            [.. dashboard.OrderTotals.Select(row => new OrderTotalsRow(
                row.Window, row.Channel, row.Currency, row.OrderCount, row.TotalMinor))],
            new PendingPayments(
                dashboard.PendingWebsitePayments.Count,
                [.. dashboard.PendingWebsitePayments.AmountsByCurrency.Select(
                    amount => new CurrencyAmount(amount.Currency, amount.AmountMinor))],
                dashboard.PendingWebsitePayments.OldestCreatedAt),
            Window(dashboard.Composition24h),
            Window(dashboard.Composition7d),
            [.. dashboard.RecentFailures.Select(failure => new CompositionFailure(
                failure.At, failure.ErrorCode, failure.Currency, failure.TargetMinor,
                failure.DurationMs, failure.PartnerId))]);
    }

    public async Task<IReadOnlyList<OrderRow>> ListCustomerOrdersAsync(
        long customerId, int limit, CancellationToken ct = default) =>
        [.. (await sales.ListCustomerOrdersAsync(customerId, limit, ct)).Select(Row)];

    public async Task<Result<OrderSheet>> GetOrderAsync(
        long orderId, CancellationToken ct = default)
    {
        var order = await sales.GetOrderAsync(orderId, ct);

        // A provider's refusal travels unchanged: Admin has nothing to add to "no such order",
        // and translating it would only risk turning a 404 into a 500.
        if (order.IsFailure) return Result.Failure<OrderSheet>(order.Error!);

        var detail = order.Value!;

        return Result.Success(new OrderSheet(
            Row(detail.Summary),
            detail.ExternalCustomerId,
            detail.UsdEquivalentMinor,
            detail.CorrelationId,
            detail.Lines.Count,
            [.. detail.Payments.Select(payment => new PaymentView(
                payment.Id, payment.Source, payment.Method, payment.Reference,
                payment.VerifiedBy, payment.RecordedAt, payment.AmountMinor))],
            [.. detail.Documents.Select(document => new DocumentView(
                document.Id,
                document.Type,
                document.Number,
                document.IssuedAt,
                document.RelatedDocumentId,
                PdfPath(document.Id)))],
            [.. detail.Refunds.Select(refund => new RefundView(
                refund.Id, refund.AmountMinor, refund.Reason, refund.RecordedBy,
                refund.RecordedAt, refund.ExternalReference, refund.CreditNoteDocumentId))]));
    }

    private static OrderRow Row(AdminOrderListItemDto item) => new(
        item.Id,
        item.Number,
        item.Channel,
        item.PartnerId,
        item.ExternalOrderId,
        item.CustomerId,
        item.Currency,
        item.TotalMinor,
        item.RefundedMinor,
        Status(item.Status),
        item.CreatedAt);

    private static OrderStatusView Status(OrderStatusDto status) => new(
        status.Order,
        status.Payment,
        status.PaymentSource,
        status.Fulfillment,
        status.FulfillmentSource,
        status.FulfillmentStatement,
        status.Documents);

    private static CompositionWindow Window(CompositionStatsDto stats) => new(
        stats.Window, stats.Attempts, stats.Succeeded, stats.SuccessRate, stats.P95DurationMs);

    /// <summary>Where an administrator fetches this document's PDF.</summary>
    /// <remarks>
    /// Built here rather than carried by Sales, because which route may serve a document is a
    /// property of who is reading it. The partner and shop surfaces have routes of their own.
    /// </remarks>
    private static string PdfPath(long documentId) =>
        $"/api/v1/admin/documents/{documentId.ToString(CultureInfo.InvariantCulture)}/pdf";
}
