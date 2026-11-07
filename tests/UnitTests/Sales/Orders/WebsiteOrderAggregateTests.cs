using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Orders;

/// <summary>
/// The website order's lifecycle: ordered, then paid when someone has actually looked.
/// </summary>
/// <remarks>
/// The mirror image of the partner path. A partner order is complete the moment it exists; a
/// website order is deliberately incomplete until an administrator confirms the money arrived,
/// because nothing in this system can see a bank account.
/// </remarks>
public sealed class WebsiteOrderAggregateTests
{
    [Fact]
    public void A_Checkout_Is_Unpaid_And_Carries_Only_A_Pro_Forma()
    {
        var order = OrderMother.WebsiteOrder();

        order.Channel.Should().Be(OrderChannel.Website);
        order.Status.Should().Be(OrderStatus.PendingPayment);
        order.PaymentStatus.Should().Be(PaymentStatus.Unpaid);
        order.PaymentSource.Should().BeNull("nobody has said any money moved");
        order.FulfillmentStatus.Should().Be(FulfillmentStatus.Unfulfilled);
        order.FulfillmentSource.Should().BeNull();

        var document = order.Documents.Should().ContainSingle().Subject;
        document.Type.Should().Be(DocumentType.ProForma);
        document.Number.Should().StartWith("PF-");
        document.Content.Title.Should().Be("Pro Forma Invoice");
        document.Content.PaymentInstruction.Should().Be(OrderMother.Instruction,
            "the buyer must keep paying against what they were given, not against today's bank details");

        order.DocumentStatus.Should().Be(DocumentStatus.ProFormaIssued);
        order.Payments.Should().BeEmpty();
    }

    [Fact]
    public void A_Website_Line_Must_Be_At_The_Catalogue_Price()
    {
        var order = OrderMother.WebsiteOrder();

        order.Lines.Should().OnlyContain(line => !line.BandEnforced,
            "the band is a composition rule, and nothing was composed here");

        var shifted = new[]
        {
            OrderMother.Line(1, 10, quantity: 1, unitPriceMinor: 1_010, referenceMinor: 1_000),
        };

        var act = () => OrderMother.WebsiteOrder(shifted);

        // Well inside ±5%, and still refused: a website customer pays what they were shown.
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("ORDER_LINE_PRICE_NOT_REFERENCE");
    }

    [Fact]
    public void Confirming_Payment_Invoices_And_Leaves_The_Pro_Forma_Alone()
    {
        var order = OrderMother.WebsiteOrder();
        var proFormaBefore = order.Documents.Single().Content;

        order.ConfirmExternalPayment(
            paymentRecordId: 9002,
            OrderMother.Invoice(),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "bank-transfer",
            "TT-1",
            OrderMother.Seller,
            OrderMother.RequiredBuyerFields,
            OrderMother.NoTax,
            OrderMother.Now.AddHours(2));

        order.Status.Should().Be(OrderStatus.Completed);
        order.PaymentStatus.Should().Be(PaymentStatus.Paid);
        order.PaymentSource.Should().Be(PaymentSource.AdminConfirmed);
        order.FulfillmentStatus.Should().Be(FulfillmentStatus.Fulfilled);
        order.FulfillmentSource.Should().Be(FulfillmentSource.SystemAutomatic);

        var payment = order.Payments.Should().ContainSingle().Subject;
        payment.Source.Should().Be(PaymentSource.AdminConfirmed);
        payment.Method.Should().Be("bank-transfer");
        payment.Reference.Should().Be("TT-1");
        payment.VerifiedBy.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        payment.AmountMinor.Should().Be(order.TotalMinor);

        var invoice = order.CommercialInvoice;
        invoice.Content.Title.Should().Be("Invoice");
        invoice.Content.Buyer.Should().Be(order.Buyer);

        order.Documents.Single(d => d.Type == DocumentType.ProForma).Content
            .Should().Be(proFormaBefore, "the buyer holds a copy of that document");
    }

    [Fact]
    public void Payment_Cannot_Be_Confirmed_Twice()
    {
        var order = OrderMother.PaidWebsiteOrder();

        var act = () => order.ConfirmExternalPayment(
            9003, OrderMother.Invoice(), Guid.NewGuid(), "bank-transfer", null,
            OrderMother.Seller, OrderMother.RequiredBuyerFields, OrderMother.NoTax,
            OrderMother.Now.AddHours(3));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("ORDER_STATE_CONFLICT");
    }

    [Fact]
    public void An_Unpaid_Order_Cancels_Idempotently_And_A_Paid_One_Does_Not_Cancel()
    {
        var order = OrderMother.WebsiteOrder();

        order.Cancel(OrderMother.Now);
        order.Cancel(OrderMother.Now);

        order.Status.Should().Be(OrderStatus.Cancelled,
            "asking twice for a state the order is already in is not a failure");

        var paid = OrderMother.PaidWebsiteOrder();
        var act = () => paid.Cancel(OrderMother.Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("ORDER_STATE_CONFLICT");
    }
}
