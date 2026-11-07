using System.Text.Json;
using EwaysVNext.Core.Application;
using EwaysVNext.Database;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Sales.QueryApplication.Partner;

/// <summary>
/// Partner-scoped reads, projected straight from the shared context.
/// </summary>
/// <remarks>
/// Every query begins by narrowing to this partner's own orders. That narrowing is in the SQL,
/// not a check afterwards — a projection that loaded first and compared later would already have
/// read another partner's invoice into this process before deciding not to return it.
/// </remarks>
internal sealed class PartnerOrderQueries(EwaysVNextDbContext db) : IPartnerOrderQueries
{
    private const int MaxLimit = 200;
    private const int MaxSearchIds = 100;

    private static readonly Error NotFound = new("NOT_FOUND", "No such order.", 404);

    public async Task<Result<PartnerOrderDto>> GetAsync(
        PartnerPrincipal principal, long orderId, CancellationToken ct = default)
    {
        var order = await Detail(Scoped(principal).Where(o => o.Id == orderId))
            .FirstOrDefaultAsync(ct);

        return order is null ? Result.Failure<PartnerOrderDto>(NotFound) : Result.Success(Map(order));
    }

    public async Task<Result<PartnerOrderDto>> GetByExternalIdAsync(
        PartnerPrincipal principal, string externalOrderId, CancellationToken ct = default)
    {
        var order = await Detail(Scoped(principal).Where(o => o.ExternalOrderId == externalOrderId))
            .FirstOrDefaultAsync(ct);

        return order is null ? Result.Failure<PartnerOrderDto>(NotFound) : Result.Success(Map(order));
    }

    public async Task<IReadOnlyList<PartnerOrderDto>> GetManyAsync(
        PartnerPrincipal principal, IReadOnlyCollection<long> orderIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);

        var rows = await Detail(Scoped(principal).Where(o => orderIds.Contains(o.Id)))
            .ToListAsync(ct);

        return [.. rows.Select(Map)];
    }

    public async Task<Result<CursorPage<PartnerOrderSummaryDto>>> ListAsync(
        PartnerPrincipal principal, PartnerOrderListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit is < 1 or > MaxLimit)
        {
            return Result.Failure<CursorPage<PartnerOrderSummaryDto>>(
                new Error("VALIDATION_FAILED", $"limit must be between 1 and {MaxLimit}.", 400));
        }

        var orders = Scoped(principal);

        if (query.ExternalOrderId is { } externalOrderId)
            orders = orders.Where(o => o.ExternalOrderId == externalOrderId);

        if (query.ExternalCustomerId is { } externalCustomerId)
            orders = orders.Where(o => o.ExternalCustomerId == externalCustomerId);

        if (query.CustomerId is { } customerId)
            orders = orders.Where(o => o.CustomerId == customerId);

        if (query.Currency is { } currency)
            orders = orders.Where(o => o.Currency == currency);

        if (query.From is { } from) orders = orders.Where(o => o.CreatedAt >= from);
        if (query.To is { } to) orders = orders.Where(o => o.CreatedAt <= to);

        return Result.Success(await PageAsync(orders, query.Cursor, query.Limit, ct));
    }

    public async Task<Result<CursorPage<PartnerOrderSummaryDto>>> ListForCustomerAsync(
        PartnerPrincipal principal, CustomerKey customer, long? cursor, int limit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        if (limit is < 1 or > MaxLimit)
        {
            return Result.Failure<CursorPage<PartnerOrderSummaryDto>>(
                new Error("VALIDATION_FAILED", $"limit must be between 1 and {MaxLimit}.", 400));
        }

        var orders = customer switch
        {
            CustomerKey.ById byId => Scoped(principal).Where(o => o.CustomerId == byId.CustomerId),
            CustomerKey.ByExternalId byExternal =>
                Scoped(principal).Where(o => o.ExternalCustomerId == byExternal.ExternalCustomerId),
            _ => throw new ArgumentOutOfRangeException(nameof(customer), customer, "Unknown key."),
        };

        // A customer with no orders for this partner is indistinguishable from one that does not
        // exist — which is the point. Answering "exists, but not yours" would let a partner
        // enumerate another's customers.
        if (!await orders.AnyAsync(ct))
            return Result.Failure<CursorPage<PartnerOrderSummaryDto>>(NotFound);

        return Result.Success(await PageAsync(orders, cursor, limit, ct));
    }

    public async Task<Result<PartnerInvoiceDto>> GetInvoiceAsync(
        PartnerPrincipal principal, long documentId, CancellationToken ct = default)
    {
        var row = await Scoped(principal)
            .SelectMany(o => o.Documents
                .Where(d => d.Id == documentId)
                .Select(d => new InvoiceRow(
                    d.Id, d.Type, d.Number, o.Id, o.ExternalOrderId!, d.IssuedAt, d.Content)))
            .FirstOrDefaultAsync(ct);

        return row is null
            ? Result.Failure<PartnerInvoiceDto>(NotFound)
            : Result.Success(Map(row));
    }

    public async Task<Result<IReadOnlyList<InvoiceSearchItem>>> SearchInvoicesAsync(
        PartnerPrincipal principal, IReadOnlyList<string> externalOrderIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(externalOrderIds);

        var distinct = externalOrderIds.Distinct(StringComparer.Ordinal).ToList();

        if (distinct.Count is 0 or > MaxSearchIds)
        {
            return Result.Failure<IReadOnlyList<InvoiceSearchItem>>(new Error(
                "VALIDATION_FAILED",
                $"Search between 1 and {MaxSearchIds} distinct external order ids.",
                400));
        }

        var rows = await Scoped(principal)
            .Where(o => distinct.Contains(o.ExternalOrderId!))
            .SelectMany(o => o.Documents.Select(d => new SearchRow(
                o.ExternalOrderId!,
                new InvoiceRow(d.Id, d.Type, d.Number, o.Id, o.ExternalOrderId!, d.IssuedAt, d.Content))))
            .ToListAsync(ct);

        var byExternalId = rows
            .GroupBy(row => row.ExternalOrderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        // In the order asked, including the ones that found nothing: a partner reconciling a
        // thousand orders needs to know which this system has never heard of.
        return Result.Success<IReadOnlyList<InvoiceSearchItem>>(
        [
            .. externalOrderIds.Select(id => byExternalId.TryGetValue(id, out var found)
                ? new InvoiceSearchItem(id, true, [.. found.Select(row => Map(row.Invoice))])
                : new InvoiceSearchItem(id, false, [])),
        ]);
    }

    /// <summary>
    /// This partner's own orders, and nothing else. Every query starts here.
    /// </summary>
    private IQueryable<Order> Scoped(PartnerPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return db.Orders
            .AsNoTracking()
            .Where(o => o.Channel == OrderChannel.PartnerApi && o.PartnerId == principal.PartnerId);
    }

    /// <summary>
    /// Projects orders that have <b>already</b> been narrowed.
    /// </summary>
    /// <remarks>
    /// It takes the narrowed queryable rather than the principal because a filter applied after
    /// the projection runs against the DTO, which EF cannot translate — and the shape that makes
    /// that mistake impossible is the one that takes the filtered source.
    /// </remarks>
    private static IQueryable<DetailRow> Detail(IQueryable<Order> orders) =>
        orders.Select(o => new DetailRow(
            o.Id,
            o.Number,
            o.ExternalOrderId!,
            o.CustomerId,
            o.ExternalCustomerId!,
            o.Currency,
            o.TotalMinor,
            o.SubtotalMinor,
            o.TaxMinor,
            o.Status,
            o.PaymentStatus,
            o.PaymentSource,
            o.FulfillmentStatus,
            o.FulfillmentSource,
            o.CreatedAt,
            o.CorrelationId,
            o.Lines.OrderBy(l => l.LineNo).Select(l => new LineRow(
                l.LineNo, l.VariantId, l.ProductId, l.Sku, l.ProductTitle, l.BrandName,
                l.CategoryPath, l.Attributes, l.Quantity, l.UnitPriceMinor, l.LineTotalMinor,
                l.Price.ReferenceMinor, l.DeviationBps, l.Price.Source)).ToList(),
            o.Documents.Select(d => new InvoiceRow(
                d.Id, d.Type, d.Number, o.Id, o.ExternalOrderId!, d.IssuedAt, d.Content)).ToList()));

    private static async Task<CursorPage<PartnerOrderSummaryDto>> PageAsync(
        IQueryable<Order> orders, long? cursor, int limit, CancellationToken ct)
    {
        if (cursor is { } after) orders = orders.Where(o => o.Id < after);

        var rows = await orders
            .OrderByDescending(o => o.Id)
            .Take(limit + 1)
            .Select(o => new SummaryRow(
                o.Id,
                o.Number,
                o.ExternalOrderId!,
                o.CustomerId,
                o.ExternalCustomerId!,
                o.Currency,
                o.TotalMinor,
                o.Status,
                o.PaymentStatus,
                o.PaymentSource,
                o.FulfillmentStatus,
                o.FulfillmentSource,
                o.CreatedAt,
                o.Documents
                    .Where(d => d.Type == DocumentType.Commercial)
                    .Select(d => new DocumentStub(d.Id, d.Number))
                    .FirstOrDefault(),
                o.Documents.Select(d => d.Type).ToList()))
            .ToListAsync(ct);

        // One row past the page is the cheapest evidence that another page exists.
        var hasMore = rows.Count > limit;
        var page = rows.Take(limit).ToList();

        return new CursorPage<PartnerOrderSummaryDto>(
            [.. page.Select(Map)],
            hasMore && page.Count > 0 ? page[^1].Id : null);
    }

    private static PartnerOrderDto Map(DetailRow row) => new(
        row.Id,
        row.Number,
        row.ExternalOrderId,
        row.CustomerId,
        row.ExternalCustomerId,
        row.Currency.Trim(),
        row.TotalMinor,
        row.SubtotalMinor,
        row.TaxMinor,
        Status(
            row.Status, row.PaymentStatus, row.PaymentSource, row.FulfillmentStatus,
            row.FulfillmentSource, row.Documents.Select(d => d.Type)),
        [.. row.Lines.Select(line => new OrderLineDto(
            line.LineNo, line.VariantId, line.ProductId, line.Sku, line.ProductTitle,
            line.BrandName, line.CategoryPath, line.Attributes, line.Quantity,
            line.UnitPriceMinor, line.LineTotalMinor, line.ReferenceMinor, line.DeviationBps,
            line.PriceSource.ToString()))],
        row.Documents
            .Where(d => d.Type == DocumentType.Commercial)
            .Select(Document)
            .FirstOrDefault(),
        [.. row.Documents.Where(d => d.Type == DocumentType.CreditNote).Select(Document)],
        row.CreatedAt,
        row.CorrelationId);

    private static PartnerOrderSummaryDto Map(SummaryRow row) => new(
        row.Id,
        row.Number,
        row.ExternalOrderId,
        row.CustomerId,
        row.ExternalCustomerId,
        row.Currency.Trim(),
        row.TotalMinor,
        Status(
            row.Status, row.PaymentStatus, row.PaymentSource, row.FulfillmentStatus,
            row.FulfillmentSource, row.DocumentTypes),
        row.Invoice?.Id,
        row.Invoice?.Number,
        row.CreatedAt);

    private static DocumentDto Document(InvoiceRow row) => new(
        row.Id,
        row.Type.ToString(),
        row.Number,
        row.IssuedAt,
        RelatedDocumentId: null,
        JsonSerializer.SerializeToElement(row.Content, DocumentContentJson.Options));

    private static PartnerInvoiceDto Map(InvoiceRow row) => new(
        row.Id,
        row.Type.ToString(),
        row.Number,
        row.OrderId,
        row.ExternalOrderId,
        row.IssuedAt,
        JsonSerializer.SerializeToElement(row.Content, DocumentContentJson.Options));

    /// <summary>
    /// The document status is derived from the documents, here as on the aggregate, so the two
    /// can never disagree.
    /// </summary>
    private static OrderStatusDto Status(
        OrderStatus order,
        PaymentStatus payment,
        PaymentSource? paymentSource,
        FulfillmentStatus fulfillment,
        FulfillmentSource? fulfillmentSource,
        IEnumerable<DocumentType> documentTypes)
    {
        var types = documentTypes.ToList();

        var documents =
            types.Contains(DocumentType.CreditNote) ? DocumentStatus.CreditNoted
            : types.Contains(DocumentType.Commercial) ? DocumentStatus.Invoiced
            : types.Contains(DocumentType.ProForma) ? DocumentStatus.ProFormaIssued
            : DocumentStatus.None;

        return new OrderStatusDto(
            order.ToString(),
            payment.ToString(),
            paymentSource?.ToString(),
            fulfillment.ToString(),
            fulfillmentSource?.ToString(),
            DocumentWording.FulfillmentStatement,
            documents.ToString());
    }

    private sealed record DocumentStub(long Id, string Number);

    private sealed record InvoiceRow(
        long Id, DocumentType Type, string Number, long OrderId, string ExternalOrderId,
        DateTimeOffset IssuedAt, DocumentContent Content);

    private sealed record SearchRow(string ExternalOrderId, InvoiceRow Invoice);

    private sealed record LineRow(
        int LineNo, long VariantId, long ProductId, string Sku, string ProductTitle,
        string BrandName, IReadOnlyList<string> CategoryPath,
        IReadOnlyDictionary<string, string> Attributes, int Quantity, long UnitPriceMinor,
        long LineTotalMinor, long ReferenceMinor, int DeviationBps, LinePriceSource PriceSource);

    private sealed record DetailRow(
        long Id, string Number, string ExternalOrderId, long CustomerId, string ExternalCustomerId,
        string Currency, long TotalMinor, long SubtotalMinor, long TaxMinor, OrderStatus Status,
        PaymentStatus PaymentStatus, PaymentSource? PaymentSource,
        FulfillmentStatus FulfillmentStatus, FulfillmentSource? FulfillmentSource,
        DateTimeOffset CreatedAt, string CorrelationId,
        IReadOnlyList<LineRow> Lines, IReadOnlyList<InvoiceRow> Documents);

    private sealed record SummaryRow(
        long Id, string Number, string ExternalOrderId, long CustomerId, string ExternalCustomerId,
        string Currency, long TotalMinor, OrderStatus Status, PaymentStatus PaymentStatus,
        PaymentSource? PaymentSource, FulfillmentStatus FulfillmentStatus,
        FulfillmentSource? FulfillmentSource, DateTimeOffset CreatedAt,
        DocumentStub? Invoice, IReadOnlyList<DocumentType> DocumentTypes);
}
