using System.Text.Json;
using EwaysVNext.Database;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Sales.QueryApplication.Admin;

/// <summary>
/// Everything the backoffice reads about orders. <b>These names are fixed</b> — phase 6 composes
/// its dashboard from them.
/// </summary>
public interface ISalesAdminQueries
{
    Task<Result<CursorPage<AdminOrderListItemDto>>> ListOrdersAsync(
        AdminOrderFilter filter, CancellationToken ct = default);

    Task<Result<AdminOrderDetailDto>> GetOrderAsync(long orderId, CancellationToken ct = default);

    Task<Result<DocumentDto>> GetDocumentAsync(long documentId, CancellationToken ct = default);

    /// <summary>One customer's orders, newest first, for the customer-360 view.</summary>
    Task<IReadOnlyList<AdminOrderListItemDto>> ListCustomerOrdersAsync(
        long customerId, int limit, CancellationToken ct = default);

    Task<SalesDashboardDto> GetDashboardAsync(DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>
/// Admin reads across both channels, projected from the shared context.
/// </summary>
/// <remarks>
/// Unscoped by design: an administrator sees every order, which is the whole point of the
/// surface. The gate is at the endpoint — twice, by realm policy and permission — rather than in
/// the query, because there is no narrower set that would still answer the question.
/// </remarks>
internal sealed class SalesAdminQueries(EwaysVNextDbContext db) : ISalesAdminQueries
{
    private const int MaxPageSize = 200;
    private const int MaxRecentFailures = 20;

    private static readonly Error NotFound = new("NOT_FOUND", "No such order.", 404);

    public async Task<Result<CursorPage<AdminOrderListItemDto>>> ListOrdersAsync(
        AdminOrderFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.PageSize is < 1 or > MaxPageSize)
        {
            return Result.Failure<CursorPage<AdminOrderListItemDto>>(new Error(
                "VALIDATION_FAILED", $"pageSize must be between 1 and {MaxPageSize}.", 400));
        }

        var orders = Apply(db.Orders.AsNoTracking(), filter);

        if (filter.Cursor is { } after) orders = orders.Where(o => o.Id < after);

        var rows = await Summaries(orders.OrderByDescending(o => o.Id).Take(filter.PageSize + 1))
            .ToListAsync(ct);

        var hasMore = rows.Count > filter.PageSize;
        var page = rows.Take(filter.PageSize).ToList();

        return Result.Success(new CursorPage<AdminOrderListItemDto>(
            [.. page.Select(Map)],
            hasMore && page.Count > 0 ? page[^1].Id : null));
    }

    public async Task<Result<AdminOrderDetailDto>> GetOrderAsync(
        long orderId, CancellationToken ct = default)
    {
        var row = await db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new DetailRow(
                o.Id,
                o.ExternalCustomerId,
                o.UsdEquivalentMinor,
                o.CorrelationId,
                o.Buyer,
                o.Lines.OrderBy(l => l.LineNo).Select(l => new LineRow(
                    l.LineNo, l.VariantId, l.ProductId, l.Sku, l.ProductTitle, l.BrandName,
                    l.CategoryPath, l.Attributes, l.Quantity, l.UnitPriceMinor, l.LineTotalMinor,
                    l.Price.ReferenceMinor, l.DeviationBps, l.Price.Source)).ToList(),
                o.Payments.OrderBy(p => p.RecordedAt).Select(p => new AdminPaymentDto(
                    p.Id, p.Source.ToString(), p.Method, p.Reference, p.VerifiedBy,
                    p.RecordedAt, p.AmountMinor)).ToList(),
                o.Documents.OrderBy(d => d.IssuedAt).ThenBy(d => d.Id).Select(d => new DocumentRow(
                    d.Id, d.Type, d.Number, d.IssuedAt, d.RelatedDocumentId, d.Content)).ToList(),
                o.Refunds.OrderBy(r => r.RecordedAt).Select(r => new AdminRefundDto(
                    r.Id, r.AmountMinor, r.Reason, r.RecordedBy, r.RecordedAt,
                    r.ExternalReference, r.CreditNoteDocumentId)).ToList(),
                o.CompositionTrace))
            .FirstOrDefaultAsync(ct);

        if (row is null) return Result.Failure<AdminOrderDetailDto>(NotFound);

        var summary = await Summaries(db.Orders.AsNoTracking().Where(o => o.Id == orderId))
            .FirstAsync(ct);

        return Result.Success(new AdminOrderDetailDto(
            Map(summary),
            row.ExternalCustomerId,
            row.UsdEquivalentMinor,
            row.CorrelationId,
            JsonSerializer.SerializeToElement(row.Buyer, DocumentContentJson.Options),
            [.. row.Lines.Select(line => new OrderLineDto(
                line.LineNo, line.VariantId, line.ProductId, line.Sku, line.ProductTitle,
                line.BrandName, line.CategoryPath, line.Attributes, line.Quantity,
                line.UnitPriceMinor, line.LineTotalMinor, line.ReferenceMinor, line.DeviationBps,
                line.PriceSource.ToString()))],
            row.Payments,
            [.. row.Documents.Select(Document)],
            row.Refunds,
            row.Trace is null ? null : new AdminCompositionTraceDto(
                row.Trace.Seed,
                row.Trace.StrategyVersion,
                row.Trace.Iterations,
                row.Trace.DurationMs,
                row.Trace.CandidatePoolSize,
                JsonDocument.Parse(row.Trace.Attempts).RootElement.Clone())));
    }

    public async Task<Result<DocumentDto>> GetDocumentAsync(
        long documentId, CancellationToken ct = default)
    {
        var row = await db.Orders
            .AsNoTracking()
            .SelectMany(o => o.Documents
                .Where(d => d.Id == documentId)
                .Select(d => new DocumentRow(
                    d.Id, d.Type, d.Number, d.IssuedAt, d.RelatedDocumentId, d.Content)))
            .FirstOrDefaultAsync(ct);

        return row is null
            ? Result.Failure<DocumentDto>(new Error("NOT_FOUND", "No such document.", 404))
            : Result.Success(Document(row));
    }

    public async Task<IReadOnlyList<AdminOrderListItemDto>> ListCustomerOrdersAsync(
        long customerId, int limit, CancellationToken ct = default)
    {
        var rows = await Summaries(db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.Id)
            .Take(Math.Clamp(limit, 1, MaxPageSize)))
            .ToListAsync(ct);

        return [.. rows.Select(Map)];
    }

    public async Task<SalesDashboardDto> GetDashboardAsync(
        DateTimeOffset now, CancellationToken ct = default)
    {
        var day = now.AddDays(-1);
        var week = now.AddDays(-7);

        var totals = new List<ChannelCurrencyTotalsDto>();
        totals.AddRange(await TotalsAsync("24h", day, ct));
        totals.AddRange(await TotalsAsync("7d", week, ct));

        var pending = await db.Orders
            .AsNoTracking()
            .Where(o => o.Channel == OrderChannel.Website && o.Status == OrderStatus.PendingPayment)
            .GroupBy(o => o.Currency)
            .Select(g => new
            {
                Currency = g.Key,
                Count = g.Count(),
                Amount = g.Sum(o => o.TotalMinor),
                Oldest = g.Min(o => o.CreatedAt),
            })
            .ToListAsync(ct);

        return new SalesDashboardDto(
            totals,
            new PendingWebsitePaymentsDto(
                pending.Sum(row => row.Count),
                [.. pending.Select(row => new CurrencyAmountDto(row.Currency.Trim(), row.Amount))],
                pending.Count == 0 ? null : pending.Min(row => row.Oldest)),
            await CompositionAsync("24h", day, ct),
            await CompositionAsync("7d", week, ct),
            await db.CompositionAttempts
                .AsNoTracking()
                .Where(a => !a.Succeeded)
                .OrderByDescending(a => a.At)
                .Take(MaxRecentFailures)
                .Select(a => new CompositionFailureDto(
                    a.At, a.ErrorCode!, a.Currency, a.TargetMinor, a.DurationMs, a.PartnerId))
                .ToListAsync(ct));
    }

    private async Task<List<ChannelCurrencyTotalsDto>> TotalsAsync(
        string window, DateTimeOffset since, CancellationToken ct)
    {
        var rows = await db.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= since && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => new { o.Channel, o.Currency })
            .Select(g => new
            {
                g.Key.Channel,
                g.Key.Currency,
                Count = g.Count(),
                Total = g.Sum(o => o.TotalMinor),
            })
            .ToListAsync(ct);

        return
        [
            .. rows.Select(row => new ChannelCurrencyTotalsDto(
                window, row.Channel.ToString(), row.Currency.Trim(), row.Count, row.Total)),
        ];
    }

    /// <summary>
    /// Attempts, successes and the 95th percentile duration over a window.
    /// </summary>
    /// <remarks>
    /// Raw SQL because <c>percentile_cont</c> has no LINQ translation, and computing it in
    /// memory would mean pulling every attempt row out of the database to find one number. This
    /// statement is Postgres-specific, so it is covered only by the integration suite.
    /// </remarks>
    private async Task<CompositionStatsDto> CompositionAsync(
        string window, DateTimeOffset since, CancellationToken ct)
    {
        var row = await db.Database
            .SqlQuery<CompositionStatsRow>($"""
                SELECT count(*)::int AS attempts,
                       count(*) FILTER (WHERE succeeded)::int AS succeeded,
                       round(percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_ms))::bigint AS p95
                FROM sales.composition_attempts
                WHERE at >= {since}
                """)
            .SingleAsync(ct);

        return new CompositionStatsDto(
            window,
            row.Attempts,
            row.Succeeded,
            row.Attempts == 0 ? 0m : Math.Round((decimal)row.Succeeded / row.Attempts, 4),
            row.Attempts == 0 ? null : row.P95);
    }

    private static IQueryable<SummaryRow> Summaries(IQueryable<Order> orders) =>
        orders.Select(o => new SummaryRow(
            o.Id,
            o.Number,
            o.Channel,
            o.PartnerId,
            o.ExternalOrderId,
            o.CustomerId,
            o.Currency,
            o.TotalMinor,
            o.Refunds.Sum(r => (long?)r.AmountMinor) ?? 0,
            o.Status,
            o.PaymentStatus,
            o.PaymentSource,
            o.FulfillmentStatus,
            o.FulfillmentSource,
            o.Documents.Select(d => d.Type).ToList(),
            o.CreatedAt));

    private static IQueryable<Order> Apply(IQueryable<Order> orders, AdminOrderFilter filter)
    {
        if (filter.Channel is { } channel)
        {
            var value = channel == AdminChannelFilter.Website
                ? OrderChannel.Website
                : OrderChannel.PartnerApi;

            orders = orders.Where(o => o.Channel == value);
        }

        if (filter.OrderStatus is { } orderStatus
            && Enum.TryParse<OrderStatus>(orderStatus, out var parsedOrder))
        {
            orders = orders.Where(o => o.Status == parsedOrder);
        }

        if (filter.PaymentStatus is { } paymentStatus
            && Enum.TryParse<PaymentStatus>(paymentStatus, out var parsedPayment))
        {
            orders = orders.Where(o => o.PaymentStatus == parsedPayment);
        }

        if (filter.Currency is { } currency) orders = orders.Where(o => o.Currency == currency);
        if (filter.CustomerId is { } customerId) orders = orders.Where(o => o.CustomerId == customerId);
        if (filter.PartnerId is { } partnerId) orders = orders.Where(o => o.PartnerId == partnerId);
        if (filter.Number is { } number) orders = orders.Where(o => o.Number == number);
        if (filter.From is { } from) orders = orders.Where(o => o.CreatedAt >= from);
        if (filter.To is { } to) orders = orders.Where(o => o.CreatedAt <= to);

        if (filter.ExternalOrderId is { } externalOrderId)
            orders = orders.Where(o => o.ExternalOrderId == externalOrderId);

        return orders;
    }

    private static AdminOrderListItemDto Map(SummaryRow row) => new(
        row.Id,
        row.Number,
        row.Channel.ToString(),
        row.PartnerId,
        row.ExternalOrderId,
        row.CustomerId,
        row.Currency.Trim(),
        row.TotalMinor,
        row.RefundedMinor,
        OrderStatusMapping.Describe(
            row.Status, row.PaymentStatus, row.PaymentSource, row.FulfillmentStatus,
            row.FulfillmentSource, row.DocumentTypes),
        row.CreatedAt);

    private static DocumentDto Document(DocumentRow row) => new(
        row.Id,
        row.Type.ToString(),
        row.Number,
        row.IssuedAt,
        row.RelatedDocumentId,
        JsonSerializer.SerializeToElement(row.Content, DocumentContentJson.Options));

    private sealed record CompositionStatsRow(int Attempts, int Succeeded, long? P95);

    private sealed record SummaryRow(
        long Id,
        string Number,
        OrderChannel Channel,
        long? PartnerId,
        string? ExternalOrderId,
        long CustomerId,
        string Currency,
        long TotalMinor,
        long RefundedMinor,
        OrderStatus Status,
        PaymentStatus PaymentStatus,
        PaymentSource? PaymentSource,
        FulfillmentStatus FulfillmentStatus,
        FulfillmentSource? FulfillmentSource,
        IReadOnlyList<DocumentType> DocumentTypes,
        DateTimeOffset CreatedAt);

    private sealed record DetailRow(
        long Id,
        string? ExternalCustomerId,
        long UsdEquivalentMinor,
        string CorrelationId,
        BuyerBlock Buyer,
        IReadOnlyList<LineRow> Lines,
        IReadOnlyList<AdminPaymentDto> Payments,
        IReadOnlyList<DocumentRow> Documents,
        IReadOnlyList<AdminRefundDto> Refunds,
        CompositionTrace? Trace);

    private sealed record LineRow(
        int LineNo,
        long VariantId,
        long ProductId,
        string Sku,
        string ProductTitle,
        string BrandName,
        IReadOnlyList<string> CategoryPath,
        IReadOnlyDictionary<string, string> Attributes,
        int Quantity,
        long UnitPriceMinor,
        long LineTotalMinor,
        long ReferenceMinor,
        int DeviationBps,
        LinePriceSource PriceSource);

    private sealed record DocumentRow(
        long Id,
        DocumentType Type,
        string Number,
        DateTimeOffset IssuedAt,
        long? RelatedDocumentId,
        DocumentContent Content);
}
