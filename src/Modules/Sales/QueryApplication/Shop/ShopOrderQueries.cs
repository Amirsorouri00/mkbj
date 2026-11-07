using System.Text.Json;
using EwaysVNext.Core.Application;
using EwaysVNext.Database;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Sales.QueryApplication.Shop;

/// <remarks>
/// The documents are the pro forma and, once an administrator has confirmed payment, the
/// commercial invoice. They are returned in issue order, which is also the order they happened.
/// </remarks>
public sealed record ShopOrderDto(
    long Id,
    string Number,
    string Currency,
    long TotalMinor,
    OrderStatusDto Status,
    IReadOnlyList<OrderLineDto> Lines,
    IReadOnlyList<DocumentDto> Documents,
    DateTimeOffset CreatedAt);

public interface IShopOrderQueries
{
    Task<CursorPage<ShopOrderDto>> ListMyOrdersAsync(
        long customerId, long? cursor, int limit, CancellationToken ct = default);

    Task<Result<ShopOrderDto>> GetMyOrderAsync(
        long customerId, long orderId, CancellationToken ct = default);
}

/// <summary>
/// A website customer's own orders.
/// </summary>
/// <remarks>
/// Every query narrows to this customer's <b>Website</b> orders in SQL. Both halves matter: the
/// customer id so nobody reads someone else's order, and the channel so a partner-placed order
/// billed to the same person does not appear in a shop history they never shopped.
/// </remarks>
internal sealed class ShopOrderQueries(EwaysVNextDbContext db) : IShopOrderQueries
{
    private const int MaxLimit = 100;

    private static readonly Error NotFound = new("NOT_FOUND", "No such order.", 404);

    public async Task<CursorPage<ShopOrderDto>> ListMyOrdersAsync(
        long customerId, long? cursor, int limit, CancellationToken ct = default)
    {
        var capped = Math.Clamp(limit, 1, MaxLimit);

        var orders = Mine(customerId);
        if (cursor is { } after) orders = orders.Where(o => o.Id < after);

        var rows = await Detail(orders.OrderByDescending(o => o.Id).Take(capped + 1))
            .ToListAsync(ct);

        // One row past the page is the cheapest evidence that another page exists.
        var hasMore = rows.Count > capped;
        var page = rows.Take(capped).ToList();

        return new CursorPage<ShopOrderDto>(
            [.. page.Select(Map)],
            hasMore && page.Count > 0 ? page[^1].Id : null);
    }

    public async Task<Result<ShopOrderDto>> GetMyOrderAsync(
        long customerId, long orderId, CancellationToken ct = default)
    {
        var row = await Detail(Mine(customerId).Where(o => o.Id == orderId))
            .FirstOrDefaultAsync(ct);

        // Someone else's order is "no such order", not "not yours": the second answer confirms
        // the order exists, which is not this customer's business.
        return row is null ? Result.Failure<ShopOrderDto>(NotFound) : Result.Success(Map(row));
    }

    private IQueryable<Order> Mine(long customerId) =>
        db.Orders
            .AsNoTracking()
            .Where(o => o.Channel == OrderChannel.Website && o.CustomerId == customerId);

    /// <remarks>
    /// Takes the already-narrowed queryable: a filter applied after the projection runs against
    /// the DTO, which EF cannot translate.
    /// </remarks>
    private static IQueryable<ShopRow> Detail(IQueryable<Order> orders) =>
        orders.Select(o => new ShopRow(
            o.Id,
            o.Number,
            o.Currency,
            o.TotalMinor,
            o.Status,
            o.PaymentStatus,
            o.PaymentSource,
            o.FulfillmentStatus,
            o.FulfillmentSource,
            o.CreatedAt,
            o.Lines.OrderBy(l => l.LineNo).Select(l => new ShopLineRow(
                l.LineNo, l.VariantId, l.ProductId, l.Sku, l.ProductTitle, l.BrandName,
                l.CategoryPath, l.Attributes, l.Quantity, l.UnitPriceMinor, l.LineTotalMinor,
                l.Price.ReferenceMinor, l.DeviationBps, l.Price.Source)).ToList(),
            o.Documents.OrderBy(d => d.IssuedAt).ThenBy(d => d.Id).Select(d => new ShopDocumentRow(
                d.Id, d.Type, d.Number, d.IssuedAt, d.RelatedDocumentId, d.Content)).ToList()));

    private static ShopOrderDto Map(ShopRow row) => new(
        row.Id,
        row.Number,
        row.Currency.Trim(),
        row.TotalMinor,
        OrderStatusMapping.Describe(
            row.Status, row.PaymentStatus, row.PaymentSource, row.FulfillmentStatus,
            row.FulfillmentSource, row.Documents.Select(d => d.Type)),
        [.. row.Lines.Select(line => new OrderLineDto(
            line.LineNo, line.VariantId, line.ProductId, line.Sku, line.ProductTitle,
            line.BrandName, line.CategoryPath, line.Attributes, line.Quantity,
            line.UnitPriceMinor, line.LineTotalMinor, line.ReferenceMinor, line.DeviationBps,
            line.PriceSource.ToString()))],
        [.. row.Documents.Select(d => new DocumentDto(
            d.Id,
            d.Type.ToString(),
            d.Number,
            d.IssuedAt,
            d.RelatedDocumentId,
            JsonSerializer.SerializeToElement(d.Content, DocumentContentJson.Options)))],
        row.CreatedAt);

    private sealed record ShopRow(
        long Id,
        string Number,
        string Currency,
        long TotalMinor,
        OrderStatus Status,
        PaymentStatus PaymentStatus,
        PaymentSource? PaymentSource,
        FulfillmentStatus FulfillmentStatus,
        FulfillmentSource? FulfillmentSource,
        DateTimeOffset CreatedAt,
        IReadOnlyList<ShopLineRow> Lines,
        IReadOnlyList<ShopDocumentRow> Documents);

    private sealed record ShopLineRow(
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

    private sealed record ShopDocumentRow(
        long Id,
        DocumentType Type,
        string Number,
        DateTimeOffset IssuedAt,
        long? RelatedDocumentId,
        DocumentContent Content);
}
