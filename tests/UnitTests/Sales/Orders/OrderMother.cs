using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;

namespace EwaysVNext.UnitTests.Sales.Orders;

/// <summary>
/// A valid partner order, and the knobs a test needs to make it invalid in exactly one way.
/// </summary>
/// <remarks>
/// Every default here is a legal value, so a failing test says something about the one thing it
/// changed. A builder whose defaults were already borderline would make every failure ambiguous.
/// </remarks>
internal static class OrderMother
{
    public static readonly DateTimeOffset Now =
        new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);

    public static readonly SellerBlock Seller = new(
        "Demo Pte. Ltd.", "202612345A", "1 Example Road", "ar@example.com", "Thank you");

    public static readonly BuyerBlock Buyer = new(
        CustomerId: 42,
        ExternalCustomerId: "C-77",
        FullName: "Pat Lim",
        CompanyName: "Lim Trading",
        Email: "pat@example.com",
        Phone: null,
        BillingAddress: "2 Buyer Street",
        RegistrationNumber: null);

    /// <summary>What a partner invoice requires by default: a way to reach the buyer.</summary>
    public static readonly string[] RequiredBuyerFields = ["email|phone"];

    public static readonly TaxAssessment NoTax = new(0, DocumentWording.NotGstRegistered);

    /// <summary>Two lines: 2 × 1,000 and 1 × 2,000, totalling 4,000.</summary>
    public static IReadOnlyList<OrderLineInput> Lines() =>
    [
        Line(lineId: 1, variantId: 10, quantity: 2, unitPriceMinor: 1_000, referenceMinor: 1_000),
        Line(lineId: 2, variantId: 11, quantity: 1, unitPriceMinor: 2_000, referenceMinor: 2_000),
    ];

    public static OrderLineInput Line(
        long lineId, long variantId, int quantity, long unitPriceMinor, long referenceMinor) =>
        new(
            lineId,
            new VariantSnapshot(
                variantId,
                ProductId: variantId * 10,
                Sku: $"SKU-{variantId}",
                ProductTitle: $"Product {variantId}",
                BrandName: "Demo Brand",
                CategoryPath: ["Kitchen", "Kettles"],
                Attributes: new Dictionary<string, string> { ["colour"] = "Red" }),
            quantity,
            unitPriceMinor,
            new PriceSnapshot(
                referenceMinor,
                LinePriceSource.Base,
                FxRate: null,
                FxRateId: null,
                FxOverrideId: null,
                FxRateAt: null,
                FxProvenance: null,
                BaseAmountMinor: referenceMinor,
                BaseCurrency: "USD"));

    public static PartnerOrderHeader Header(long requestedTotalMinor = 4_000) => new(
        OrderId: 1001,
        Number: OrderNumberFormat.Order(123),
        PartnerId: 7,
        ExternalOrderId: "PO-77",
        ExternalCustomerId: "C-77",
        RequestFingerprint: new string('a', 64),
        CustomerId: 42,
        Currency: "USD",
        requestedTotalMinor,
        UsdEquivalentMinor: requestedTotalMinor,
        CorrelationId: "corr-1");

    public static CompositionTraceInput Trace() =>
        new(Seed: 7, StrategyVersion: "v1", Iterations: 3, DurationMs: 12,
            CandidatePoolSize: 60, AttemptsJson: "[\"a0:k=2:ok\"]");

    public static IssuedDocumentRef Invoice() =>
        new(DocumentId: 5001, Number: OrderNumberFormat.Document(DocumentType.Commercial, 123));

    public static Order PartnerOrder(
        IReadOnlyList<OrderLineInput>? lines = null,
        PartnerOrderHeader? header = null,
        BuyerBlock? buyer = null,
        IReadOnlyList<string>? requiredBuyerFields = null,
        TaxAssessment? tax = null) =>
        Order.CreatePartnerOrder(
            header ?? Header(),
            lines ?? Lines(),
            Trace(),
            Invoice(),
            paymentRecordId: 9001,
            Seller,
            requiredBuyerFields ?? RequiredBuyerFields,
            buyer ?? Buyer,
            tax ?? NoTax,
            Now);

    public static readonly PaymentInstructionBlock Instruction = new(
        InstructionId: 3001, Version: 4, Text: "Pay DBS 123-456-789, quote the order number.");

    public static WebsiteOrderHeader WebsiteHeader() => new(
        OrderId: 2002,
        Number: OrderNumberFormat.Order(456),
        CustomerId: 42,
        Currency: "USD",
        UsdEquivalentMinor: 4_000,
        CorrelationId: "corr-web");

    public static IssuedDocumentRef ProForma() =>
        new(DocumentId: 6001, Number: OrderNumberFormat.Document(DocumentType.ProForma, 456));

    public static IssuedDocumentRef CreditNote(long id = 7001) =>
        new(id, OrderNumberFormat.Document(DocumentType.CreditNote, id));

    /// <summary>A placed website order: unpaid, pro forma issued, nothing fulfilled.</summary>
    public static Order WebsiteOrder(IReadOnlyList<OrderLineInput>? lines = null) =>
        Order.CheckoutWebsite(
            WebsiteHeader(),
            lines ?? Lines(),
            ProForma(),
            Seller,
            RequiredBuyerFields,
            Buyer,
            NoTax,
            Instruction,
            Now);

    /// <summary>A website order an administrator has confirmed payment on.</summary>
    public static Order PaidWebsiteOrder()
    {
        var order = WebsiteOrder();
        order.ConfirmExternalPayment(
            paymentRecordId: 9002, Invoice(), Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "bank-transfer", "TT-1", Seller, RequiredBuyerFields, NoTax, Now.AddHours(2));
        return order;
    }
}
