using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Orders;

/// <summary>
/// Money going back, as a document that points at the one it reverses.
/// </summary>
/// <remarks>
/// Accounting runs on documents that do not change once issued. A refund therefore adds a credit
/// note linked to the invoice rather than reducing it — which is also the only way a partial
/// refund can be told apart from an invoice somebody edited.
/// </remarks>
public sealed class RefundTests
{
    private static readonly Guid Admin = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void A_Partial_Refund_Credits_Against_The_Invoice_And_Leaves_It_Untouched()
    {
        var order = OrderMother.PaidWebsiteOrder();
        var invoice = order.CommercialInvoice;
        var invoiceBefore = invoice.Content;

        var refund = order.RecordRefund(
            refundId: 8001,
            OrderMother.CreditNote(),
            amountMinor: 3_000,
            reason: "Damaged in transit",
            recordedBy: Admin,
            externalReference: "RTN-9",
            OrderMother.Seller,
            OrderMother.RequiredBuyerFields,
            OrderMother.Now.AddDays(1));

        refund.AmountMinor.Should().Be(3_000);
        order.RefundedMinor.Should().Be(3_000);
        order.PaymentStatus.Should().Be(PaymentStatus.PartiallyRefunded);

        var creditNote = order.Documents.Single(d => d.Type == DocumentType.CreditNote);
        creditNote.Number.Should().StartWith("CN-");
        creditNote.RelatedDocumentId.Should().Be(invoice.Id);
        creditNote.Content.RelatedDocumentNumber.Should().Be(invoice.Number);
        creditNote.Content.RefundAmountMinor.Should().Be(3_000);
        creditNote.Content.RefundReason.Should().Be("Damaged in transit");

        invoice.Content.Should().Be(invoiceBefore, "an issued invoice is not edited");
        order.DocumentStatus.Should().Be(DocumentStatus.CreditNoted);
    }

    [Fact]
    public void Refunds_Add_Up_To_The_Total_And_No_Further()
    {
        var order = OrderMother.PaidWebsiteOrder();

        order.RecordRefund(
            8001, OrderMother.CreditNote(7001), 3_000, "Part", Admin, null,
            OrderMother.Seller, OrderMother.RequiredBuyerFields, OrderMother.Now.AddDays(1));

        order.RecordRefund(
            8002, OrderMother.CreditNote(7002), 1_000, "Rest", Admin, null,
            OrderMother.Seller, OrderMother.RequiredBuyerFields, OrderMother.Now.AddDays(2));

        order.RefundedMinor.Should().Be(4_000);
        order.PaymentStatus.Should().Be(PaymentStatus.Refunded);

        var act = () => order.RecordRefund(
            8003, OrderMother.CreditNote(7003), 1, "One cent more", Admin, null,
            OrderMother.Seller, OrderMother.RequiredBuyerFields, OrderMother.Now.AddDays(3));

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("REFUND_EXCEEDS_REFUNDABLE");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Refund_Of_Nothing_Or_Less_Is_A_Validation_Failure(long amountMinor)
    {
        var order = OrderMother.PaidWebsiteOrder();

        var act = () => order.RecordRefund(
            8001, OrderMother.CreditNote(), amountMinor, "Why", Admin, null,
            OrderMother.Seller, OrderMother.RequiredBuyerFields, OrderMother.Now.AddDays(1));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public void An_Unpaid_Order_Has_Nothing_To_Refund()
    {
        var order = OrderMother.WebsiteOrder();

        var act = () => order.RecordRefund(
            8001, OrderMother.CreditNote(), 100, "Why", Admin, null,
            OrderMother.Seller, OrderMother.RequiredBuyerFields, OrderMother.Now.AddDays(1));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("ORDER_STATE_CONFLICT");
    }

    [Fact]
    public void A_Partner_Order_Refunds_The_Same_Way()
    {
        var order = OrderMother.PartnerOrder();

        order.RecordRefund(
            8001, OrderMother.CreditNote(), 4_000, "Returned", Admin, null,
            OrderMother.Seller, OrderMother.RequiredBuyerFields, OrderMother.Now.AddDays(1));

        // Paid and invoiced the moment it existed, so there is nothing about reversing it that
        // differs from the website path.
        order.PaymentStatus.Should().Be(PaymentStatus.Refunded);
        order.Documents.Should().Contain(d => d.Type == DocumentType.CreditNote);
    }
}
