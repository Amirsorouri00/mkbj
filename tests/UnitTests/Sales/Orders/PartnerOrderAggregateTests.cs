using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Orders;

/// <summary>
/// Creating a partner order: sold, paid, fulfilled and invoiced in one indivisible act.
/// </summary>
/// <remarks>
/// The partner has already been debited on its own side by the time it calls, so an order that
/// existed but was not invoiced would be money taken with nothing to show for it. ADR-0018 meets
/// that by aggregate design rather than by a transaction, and these tests are what hold it to
/// that: there is no sequence of calls that leaves the order half-made.
/// </remarks>
public sealed class PartnerOrderAggregateTests
{
    [Fact]
    public void Totals_Are_The_Lines_And_Nothing_Else()
    {
        var order = OrderMother.PartnerOrder();

        order.TotalMinor.Should().Be(4_000);
        order.TotalMinor.Should().Be(order.Lines.Sum(line => line.LineTotalMinor));
        order.TaxMinor.Should().Be(0);
        order.SubtotalMinor.Should().Be(order.TotalMinor, "no tax is charged, so they are equal");
    }

    [Fact]
    public void The_Order_Is_Complete_The_Moment_It_Exists()
    {
        var order = OrderMother.PartnerOrder();

        order.Status.Should().Be(OrderStatus.Completed);
        order.PaymentStatus.Should().Be(PaymentStatus.Paid);
        order.PaymentSource.Should().Be(EwaysVNext.Modules.Sales.Application.Domain.Orders
            .PaymentSource.PartnerReported);
        order.FulfillmentStatus.Should().Be(FulfillmentStatus.Fulfilled);
        order.FulfillmentSource.Should().Be(EwaysVNext.Modules.Sales.Application.Domain.Orders
            .FulfillmentSource.SystemAutomatic);
        order.Channel.Should().Be(OrderChannel.PartnerApi);
    }

    [Fact]
    public void One_Payment_Record_For_The_Whole_Amount()
    {
        var order = OrderMother.PartnerOrder();

        var payment = order.Payments.Should().ContainSingle().Which;
        payment.AmountMinor.Should().Be(order.TotalMinor);
        payment.Source.Should().Be(EwaysVNext.Modules.Sales.Application.Domain.Orders
            .PaymentSource.PartnerReported);
        payment.RecordedAt.Should().Be(OrderMother.Now);
    }

    [Fact]
    public void A_Commercial_Invoice_Is_Issued_From_The_Order_Itself()
    {
        var order = OrderMother.PartnerOrder();

        var document = order.Documents.Should().ContainSingle().Which;
        document.Type.Should().Be(DocumentType.Commercial);
        document.Number.Should().StartWith("INV-");
        document.PdfObjectKey.Should().Be($"{order.Id}/{document.Number}.pdf");

        var content = document.Content;
        content.Title.Should().Be("Invoice");
        content.TaxStatement.Should().Be("Not GST registered");
        content.FulfillmentStatement.Should().Be(DocumentWording.FulfillmentStatement);
        content.Buyer.Should().Be(OrderMother.Buyer);
        content.OrderNumber.Should().Be(order.Number);
        content.TotalMinor.Should().Be(order.TotalMinor);

        // Built from the order's own lines, so the invoice cannot disagree with what was sold.
        content.Lines.Select(line => line.LineTotalMinor)
            .Should().Equal(order.Lines.Select(line => line.LineTotalMinor));

        order.DocumentStatus.Should().Be(DocumentStatus.Invoiced);
        order.CommercialInvoice.Should().Be(document);
    }

    [Fact]
    public void Every_Line_Records_How_Far_Its_Price_Moved()
    {
        // 1,050 against a reference of 1,000 is +500 bps; 1,900 against 2,000 is −500.
        var order = OrderMother.PartnerOrder(
            lines:
            [
                OrderMother.Line(1, 10, 1, 1_050, 1_000),
                OrderMother.Line(2, 11, 1, 1_900, 2_000),
            ],
            header: OrderMother.Header(2_950));

        order.Lines.Select(line => line.DeviationBps).Should().Equal(500, -500);
        order.Lines.Should().OnlyContain(line => line.BandEnforced);
        order.Lines.Select(line => line.LineNo).Should().Equal(1, 2);
    }

    [Fact]
    public void Creating_An_Order_Announces_It()
    {
        var order = OrderMother.PartnerOrder();

        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PartnerOrderCreatedDomainEvent>()
            .Which.Should().BeEquivalentTo(new { OrderId = order.Id, PartnerId = 7L });
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Refuses(string code, string because, Func<Order> create) =>
        ((Action)(() => create()))
            .Should().Throw<DomainException>(because)
            .Which.Code.Should().Be(code);

    public static TheoryData<string, string, Func<Order>> Refusals() => new()
    {
        {
            "ORDER_LINES_TOTAL_MISMATCH",
            "the partner is billed the requested amount, so lines that total anything else would "
            + "be a different sale from the one that was paid for",
            () => OrderMother.PartnerOrder(header: OrderMother.Header(3_999))
        },
        {
            "ORDER_LINE_OUTSIDE_BAND",
            "a cent below the floor is still a price the seller never agreed to",
            () => OrderMother.PartnerOrder(
                lines: [OrderMother.Line(1, 10, 1, 949, 1_000)],
                header: OrderMother.Header(949))
        },
        {
            "ORDER_LINE_OUTSIDE_BAND",
            "and a cent above the ceiling",
            () => OrderMother.PartnerOrder(
                lines: [OrderMother.Line(1, 10, 1, 1_051, 1_000)],
                header: OrderMother.Header(1_051))
        },
        {
            "ORDER_LINE_QUANTITY_INVALID",
            "a line of nothing is not a line",
            () => OrderMother.PartnerOrder(
                lines: [OrderMother.Line(1, 10, 0, 1_000, 1_000)],
                header: OrderMother.Header(0))
        },
        {
            "ORDER_LINES_EMPTY",
            "an order with no lines is an invoice for nothing",
            () => OrderMother.PartnerOrder(lines: [], header: OrderMother.Header(0))
        },
        {
            "ORDER_LINE_DUPLICATE_VARIANT",
            "the same variant twice should have been one line with a larger quantity",
            () => OrderMother.PartnerOrder(
                lines:
                [
                    OrderMother.Line(1, 10, 1, 1_000, 1_000),
                    OrderMother.Line(2, 10, 1, 1_000, 1_000),
                ],
                header: OrderMother.Header(2_000))
        },
        {
            "INVOICE_BUYER_FIELDS_MISSING",
            "an invoice to a buyer nobody can reach cannot be sent, so it is not issued",
            () => OrderMother.PartnerOrder(
                buyer: OrderMother.Buyer with { Email = null, Phone = null })
        },
    };

    [Fact]
    public void Band_Edges_Are_Legal()
    {
        var order = OrderMother.PartnerOrder(
            lines:
            [
                OrderMother.Line(1, 10, 1, 950, 1_000),
                OrderMother.Line(2, 11, 1, 2_100, 2_000),
            ],
            header: OrderMother.Header(3_050));

        order.TotalMinor.Should().Be(3_050);
    }

    [Fact]
    public void A_Buyer_Reachable_By_Phone_Alone_Is_Enough() =>
        OrderMother.PartnerOrder(
                buyer: OrderMother.Buyer with { Email = null, Phone = "+6598765432" })
            .Documents.Should().ContainSingle();

    [Fact]
    public void The_Composition_Is_Recorded_So_The_Basket_Can_Be_Reproduced()
    {
        var trace = OrderMother.PartnerOrder().CompositionTrace;

        trace.Should().NotBeNull();
        trace!.Seed.Should().Be(7);
        trace.StrategyVersion.Should().Be("v1");
        trace.CandidatePoolSize.Should().Be(60);
    }

    [Fact]
    public void An_Order_With_No_Invoice_Refuses_To_Pretend_It_Has_One()
    {
        // Reached only by a caller that has already assumed the order is invoiced; a null here
        // would turn that wrong assumption into a confusing failure somewhere further away.
        var order = OrderMother.PartnerOrder();
        var withoutDocuments = typeof(Order)
            .GetField("_documents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        ((List<OrderDocument>)withoutDocuments.GetValue(order)!).Clear();

        order.DocumentStatus.Should().Be(DocumentStatus.None);
        ((Func<OrderDocument>)(() => order.CommercialInvoice))
            .Should().Throw<DomainException>().Which.Code.Should().Be("ORDER_NOT_INVOICED");
    }
}
