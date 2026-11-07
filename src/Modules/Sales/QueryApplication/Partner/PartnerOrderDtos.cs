using System.Text.Json;

namespace EwaysVNext.Modules.Sales.QueryApplication.Partner;

public sealed record MoneyMinor(long Minor, string Currency);

public sealed record OrderLineDto(
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
    long ReferencePriceMinor,
    int DeviationBps,
    string PriceSource);

/// <remarks>
/// <c>Content</c> is the stored snapshot verbatim, as JSON. A partner reconciling an invoice
/// needs exactly what was issued, not this system's current idea of how to render it.
/// </remarks>
public sealed record DocumentDto(
    long Id,
    string Type,
    string Number,
    DateTimeOffset IssuedAt,
    long? RelatedDocumentId,
    JsonElement Content);

/// <remarks>
/// The fulfilment statement travels with the status rather than only on the document, because a
/// partner reading this over the API is making the same assumption a reader of the invoice
/// would: that "fulfilled" means something arrived. It does not.
/// </remarks>
public sealed record OrderStatusDto(
    string Order,
    string Payment,
    string? PaymentSource,
    string Fulfillment,
    string? FulfillmentSource,
    string FulfillmentStatement,
    string Documents);

public sealed record PartnerOrderDto(
    long Id,
    string Number,
    string ExternalOrderId,
    long CustomerId,
    string ExternalCustomerId,
    string Currency,
    long TotalMinor,
    long SubtotalMinor,
    long TaxMinor,
    OrderStatusDto Status,
    IReadOnlyList<OrderLineDto> Lines,
    DocumentDto? Invoice,
    IReadOnlyList<DocumentDto> CreditNotes,
    DateTimeOffset CreatedAt,
    string CorrelationId);

public sealed record PartnerOrderSummaryDto(
    long Id,
    string Number,
    string ExternalOrderId,
    long CustomerId,
    string ExternalCustomerId,
    string Currency,
    long TotalMinor,
    OrderStatusDto Status,
    long? InvoiceId,
    string? InvoiceNumber,
    DateTimeOffset CreatedAt);

/// <remarks>
/// <c>Cursor</c> is the last id of the previous page. Keyset rather than offset, for the same
/// reason the storefront pages that way: orders arrive constantly, and an offset would make a
/// partner walking its own history skip or repeat records.
/// </remarks>
public sealed record PartnerOrderListQuery(
    string? ExternalOrderId,
    string? ExternalCustomerId,
    long? CustomerId,
    string? Currency,
    DateTimeOffset? From,
    DateTimeOffset? To,
    long? Cursor,
    int Limit = 50);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, long? NextCursor);

/// <summary>A customer named either by our id or by the partner's own reference.</summary>
public abstract record CustomerKey
{
    public sealed record ById(long CustomerId) : CustomerKey;

    public sealed record ByExternalId(string ExternalCustomerId) : CustomerKey;
}

public sealed record PartnerInvoiceDto(
    long Id,
    string Type,
    string Number,
    long OrderId,
    string ExternalOrderId,
    DateTimeOffset IssuedAt,
    JsonElement Content);

/// <remarks>
/// <c>Found</c> is carried explicitly so a bulk search answers for every id it was asked about.
/// A partner reconciling a thousand orders needs to know which ones this system has never heard
/// of, which a silently shorter list would not tell it.
/// </remarks>
public sealed record InvoiceSearchItem(
    string ExternalOrderId, bool Found, IReadOnlyList<PartnerInvoiceDto> Invoices);
