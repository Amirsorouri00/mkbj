using System.Globalization;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;

namespace EwaysVNext.Modules.Sales.Application.Domain.Orders;

/// <summary>
/// One order, with its lines, payments, documents and refunds.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately one aggregate rather than several (ADR-0018). A partner order is sold, paid,
/// fulfilled and invoiced in a single indivisible act — the PRD's "atomic" requirement — and
/// meeting that by aggregate design means one save, with no transaction spanning two roots and
/// no saga to leave an order paid but uninvoiced.
/// </para>
/// <para>
/// The aggregate builds its own document content from its own lines and totals. A caller passing
/// in a prepared document could hand it one that disagrees with the order, and the invoice is
/// the thing the buyer keeps.
/// </para>
/// </remarks>
public sealed class Order : AggregateRoot<long>
{
    private readonly List<OrderLine> _lines = [];
    private readonly List<PaymentRecord> _payments = [];
    private readonly List<OrderDocument> _documents = [];
    private readonly List<Refund> _refunds = [];

    private Order() { }

    public string Number { get; private set; } = string.Empty;
    public OrderChannel Channel { get; private set; }
    public long? PartnerId { get; private set; }
    public string? ExternalOrderId { get; private set; }
    public string? ExternalCustomerId { get; private set; }
    public string? RequestFingerprint { get; private set; }
    public long CustomerId { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    public long TotalMinor { get; private set; }
    public long SubtotalMinor { get; private set; }
    public long TaxMinor { get; private set; }

    /// <summary>
    /// The order's value in USD at the time it was placed, for limits and reporting.
    /// </summary>
    /// <remarks>
    /// Frozen, not recomputed: comparing last year's orders at today's rates would make the
    /// seller's own history move.
    /// </remarks>
    public long UsdEquivalentMinor { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public PaymentSource? PaymentSource { get; private set; }
    public FulfillmentStatus FulfillmentStatus { get; private set; }
    public FulfillmentSource? FulfillmentSource { get; private set; }

    /// <summary>The buyer as they were when the order was placed, reused by every document.</summary>
    public BuyerBlock Buyer { get; private set; } = null!;

    public IReadOnlyList<OrderLine> Lines => _lines;
    public IReadOnlyList<PaymentRecord> Payments => _payments;
    public IReadOnlyList<OrderDocument> Documents => _documents;
    public IReadOnlyList<Refund> Refunds => _refunds;
    public CompositionTrace? CompositionTrace { get; private set; }

    /// <summary>Derived from the documents actually issued, so it cannot drift from them.</summary>
    public DocumentStatus DocumentStatus =>
        _documents.Any(d => d.Type == DocumentType.CreditNote) ? DocumentStatus.CreditNoted
        : _documents.Any(d => d.Type == DocumentType.Commercial) ? DocumentStatus.Invoiced
        : _documents.Any(d => d.Type == DocumentType.ProForma) ? DocumentStatus.ProFormaIssued
        : DocumentStatus.None;

    /// <summary>The commercial invoice, which a refund is issued against.</summary>
    /// <remarks>
    /// Throws rather than returning null: a caller asking for it has already decided the order is
    /// invoiced, and a null here would turn that wrong assumption into a confusing failure
    /// somewhere further away.
    /// </remarks>
    public OrderDocument CommercialInvoice =>
        _documents.FirstOrDefault(d => d.Type == DocumentType.Commercial)
        ?? throw new DomainException("ORDER_NOT_INVOICED", "This order has no commercial invoice.");

    /// <summary>
    /// Creates a partner order complete: sold, paid, fulfilled and invoiced in one act.
    /// </summary>
    /// <remarks>
    /// There is no intermediate state a caller could leave this in. The partner has already been
    /// debited on its own side by the time it calls, so an order that existed but was not
    /// invoiced would be money taken with nothing to show for it.
    /// </remarks>
    public static Order CreatePartnerOrder(
        PartnerOrderHeader header,
        IReadOnlyList<OrderLineInput> lines,
        CompositionTraceInput trace,
        IssuedDocumentRef invoice,
        long paymentRecordId,
        SellerBlock seller,
        IReadOnlyList<string> requiredBuyerFields,
        BuyerBlock buyer,
        TaxAssessment tax,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(seller);
        ArgumentNullException.ThrowIfNull(requiredBuyerFields);
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(tax);

        var order = new Order
        {
            Id = header.OrderId,
            Number = header.Number,
            Channel = OrderChannel.PartnerApi,
            PartnerId = header.PartnerId,
            ExternalOrderId = header.ExternalOrderId,
            ExternalCustomerId = header.ExternalCustomerId,
            RequestFingerprint = header.RequestFingerprint,
            CustomerId = header.CustomerId,
            Currency = header.Currency,
            UsdEquivalentMinor = header.UsdEquivalentMinor,
            CorrelationId = header.CorrelationId,
            CreatedAt = now,
            Buyer = buyer,
        };

        order.AddLines(lines, header.RequestedTotalMinor);

        order.SubtotalMinor = order.TotalMinor - tax.TaxMinor;
        order.TaxMinor = tax.TaxMinor;

        // Paid and fulfilled in the same act. The partner is billed on its own account, and
        // nothing is shipped that an operator could still stop.
        order.Status = OrderStatus.Completed;
        order.PaymentStatus = PaymentStatus.Paid;
        order.PaymentSource = Orders.PaymentSource.PartnerReported;
        order.FulfillmentStatus = FulfillmentStatus.Fulfilled;
        order.FulfillmentSource = Orders.FulfillmentSource.SystemAutomatic;

        order._payments.Add(new PaymentRecord(
            paymentRecordId, Orders.PaymentSource.PartnerReported, order.TotalMinor, now));

        order.CompositionTrace = new CompositionTrace(trace);

        order.IssueDocument(
            DocumentType.Commercial, invoice, seller, requiredBuyerFields, tax, now,
            paymentInstruction: null, related: null, refund: null);

        order.Raise(new PartnerOrderCreatedDomainEvent(order.Id, header.PartnerId));

        return order;
    }

    private void AddLines(IReadOnlyList<OrderLineInput> lines, long requestedTotalMinor)
    {
        if (lines.Count == 0)
            throw new DomainException("ORDER_LINES_EMPTY", "An order must have at least one line.");

        var seen = new HashSet<long>(lines.Count);
        long total = 0;
        var lineNo = 1;

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new DomainException(
                    "ORDER_LINE_QUANTITY_INVALID",
                    $"Line {Text(lineNo)} has a quantity of {Text(line.Quantity)}.");
            }

            if (!seen.Add(line.Variant.VariantId))
            {
                throw new DomainException(
                    "ORDER_LINE_DUPLICATE_VARIANT",
                    $"Variant {Text(line.Variant.VariantId)} appears on more than one line.");
            }

            AssertBand(line.UnitPriceMinor, line.Price.ReferenceMinor);

            var entity = new OrderLine(line, lineNo++);
            _lines.Add(entity);
            total = checked(total + entity.LineTotalMinor);
        }

        if (total != requestedTotalMinor)
        {
            throw new DomainException(
                "ORDER_LINES_TOTAL_MISMATCH",
                $"The lines total {Text(total)}, not the requested {Text(requestedTotalMinor)}.");
        }

        TotalMinor = total;
    }

    /// <summary>
    /// The ±5% band, checked once more at the aggregate boundary.
    /// </summary>
    /// <remarks>
    /// The composition engine and its verifier have both checked this already. It is checked
    /// again here because the aggregate is what the database will hold, and a line that reaches
    /// it by some other path — a future website order, a correction, a test fixture — gets the
    /// same rule. Cross-multiplied rather than divided, so no rounding enters the comparison.
    /// </remarks>
    private static void AssertBand(long unitPriceMinor, long referenceMinor)
    {
        if (100 * unitPriceMinor >= 95 * referenceMinor
            && 100 * unitPriceMinor <= 105 * referenceMinor)
        {
            return;
        }

        throw new DomainException(
            "ORDER_LINE_OUTSIDE_BAND",
            $"A unit price of {Text(unitPriceMinor)} is outside ±5% of {Text(referenceMinor)}.");
    }

    /// <summary>
    /// Builds and attaches a document from this order's own state.
    /// </summary>
    /// <remarks>
    /// The buyer-field check happens here rather than at the start of creation, because it is a
    /// rule about <b>issuing a document</b>: a channel that does not issue one immediately should
    /// not be blocked by it.
    /// </remarks>
    private void IssueDocument(
        DocumentType type,
        IssuedDocumentRef reference,
        SellerBlock seller,
        IReadOnlyList<string> requiredBuyerFields,
        TaxAssessment tax,
        DateTimeOffset issuedAt,
        PaymentInstructionBlock? paymentInstruction,
        OrderDocument? related,
        Refund? refund)
    {
        var missing = BuyerFieldRequirements.Missing(requiredBuyerFields, Buyer);
        if (missing.Count > 0)
        {
            throw new DomainException(
                "INVOICE_BUYER_FIELDS_MISSING",
                $"The buyer is missing: {string.Join(", ", missing)}.");
        }

        var content = new DocumentContent(
            Title: DocumentWording.TitleFor(type),
            DocumentType: type.ToString(),
            DocumentNumber: reference.Number,
            IssuedAt: issuedAt,
            OrderNumber: Number,
            Channel: Channel.ToString(),
            ExternalOrderId: ExternalOrderId,
            Seller: seller,
            Buyer: Buyer,
            Lines: [.. _lines.Select(line => new DocumentLine(
                line.LineNo,
                line.Sku,
                line.ProductTitle,
                line.Quantity,
                line.UnitPriceMinor,
                line.LineTotalMinor))],
            Currency: Currency,
            SubtotalMinor: SubtotalMinor,
            TaxMinor: tax.TaxMinor,
            TotalMinor: TotalMinor,
            TaxStatement: tax.Statement,
            FulfillmentStatement: DocumentWording.FulfillmentStatement,
            PaymentStatus: PaymentStatus.ToString(),
            PaymentSource: PaymentSource?.ToString(),
            PaymentInstruction: paymentInstruction,
            RelatedDocumentNumber: related?.Number,
            RefundAmountMinor: refund?.AmountMinor,
            RefundReason: refund?.Reason);

        _documents.Add(new OrderDocument(
            reference.DocumentId,
            type,
            reference.Number,
            issuedAt,
            content,
            PdfObjectKeyFor(reference.Number),
            related?.Id));
    }

    /// <summary>
    /// Where the rendered PDF lives: <c>{orderId}/{documentNumber}.pdf</c>.
    /// </summary>
    /// <remarks>
    /// Prefixed by order so one order's documents sit together in the bucket, and keyed by
    /// document number so the key says what the object is without a lookup.
    /// </remarks>
    private string PdfObjectKeyFor(string documentNumber) =>
        $"{Text(Id)}/{documentNumber}.pdf";

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
