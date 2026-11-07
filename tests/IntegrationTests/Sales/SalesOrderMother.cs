using EwaysVNext.Database;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// A real partner order in the real database, for tests about the schema rather than the model.
/// </summary>
internal static class SalesOrderMother
{
    public const long ReferenceMinor = 10_000;

    private static long _next = DateTimeOffset.UtcNow.Ticks;

    /// <summary>Ids unique within a run, since the fixture's database is shared.</summary>
    public static long NextId() => Interlocked.Increment(ref _next);

    public static readonly SellerBlock Seller = new(
        "Demo Pte. Ltd.", "202612345A", "1 Example Road", "ar@example.com", "Thank you");

    public static BuyerBlock Buyer(long customerId) => new(
        customerId, "C-77", "Pat Lim", "Lim Trading", "pat@example.com", null, "2 Buyer Street", null);

    /// <summary>
    /// A partner order. <paramref name="productId"/> is for tests that have to line up with a
    /// real catalogue row — popularity, say, which is a join on the product id.
    /// </summary>
    public static Order PartnerOrder(
        long partnerId,
        string externalOrderId,
        long quantity = 1,
        long unitPriceMinor = ReferenceMinor,
        long? productId = null,
        long? variantId = null)
    {
        var orderId = NextId();
        var total = quantity * unitPriceMinor;

        return Order.CreatePartnerOrder(
            new PartnerOrderHeader(
                orderId,
                OrderNumberFormat.Order(orderId % 1_000_000),
                partnerId,
                externalOrderId,
                ExternalCustomerId: "C-77",
                RequestFingerprint: new string('a', 64),
                CustomerId: NextId(),
                Currency: "USD",
                RequestedTotalMinor: total,
                UsdEquivalentMinor: total,
                CorrelationId: "corr-schema"),
            [
                new OrderLineInput(
                    NextId(),
                    new VariantSnapshot(
                        variantId ?? NextId(), productId ?? NextId(), "SKU-1", "A kettle", "Demo Brand",
                        ["Kitchen", "Kettles"],
                        new Dictionary<string, string> { ["colour"] = "Red" }),
                    (int)quantity,
                    unitPriceMinor,
                    new PriceSnapshot(
                        ReferenceMinor, LinePriceSource.Base, null, null, null, null, null,
                        ReferenceMinor, "USD")),
            ],
            new CompositionTraceInput(7, "v1", 3, 12, 60, "[\"a0:k=1:ok\"]"),
            new IssuedDocumentRef(NextId(), OrderNumberFormat.Document(DocumentType.Commercial, NextId() % 1_000_000)),
            paymentRecordId: NextId(),
            Seller,
            ["email|phone"],
            Buyer(orderId),
            new TaxAssessment(0, DocumentWording.NotGstRegistered),
            DateTimeOffset.UtcNow);
    }

    /// <summary>Inserts one partner order and returns the ids a schema test needs to poke at it.</summary>
    public static async Task<(long OrderId, long LineId, long ReferenceMinor, long Quantity)>
        InsertPartnerOrderAsync(IServiceProvider services, long? partnerId = null)
    {
        using var scope = services.CreateScope();

        var order = PartnerOrder(partnerId ?? NextId(), $"PO-{NextId()}");
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        (await repository.TryInsertPartnerOrderAsync(order)).Should();

        return (order.Id, order.Lines[0].Id, ReferenceMinor, order.Lines[0].Quantity);
    }

    private static void Should(this bool inserted)
    {
        if (!inserted) throw new InvalidOperationException("The order could not be inserted.");
    }
}
