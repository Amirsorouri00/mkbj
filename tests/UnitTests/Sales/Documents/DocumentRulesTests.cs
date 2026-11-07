using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Services;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Documents;

/// <summary>
/// The rules a document has to obey before it can be issued.
/// </summary>
/// <remarks>
/// These are mostly legal rather than technical: what the document may be called, what it must
/// say about tax, and which buyer details must be present. Getting one wrong produces a document
/// that is wrong in a way no amount of correct arithmetic makes up for.
/// </remarks>
public sealed class DocumentRulesTests
{
    private static readonly BuyerBlock Empty = new(
        CustomerId: 1, ExternalCustomerId: null, FullName: null, CompanyName: null,
        Email: null, Phone: null, BillingAddress: null, RegistrationNumber: null);

    [Theory]
    [InlineData(DocumentType.Commercial, "Invoice")]
    [InlineData(DocumentType.ProForma, "Pro Forma Invoice")]
    [InlineData(DocumentType.CreditNote, "Credit Note")]
    public void Title_Is_What_The_Seller_May_Call_It(DocumentType type, string expected) =>
        DocumentWording.TitleFor(type).Should().Be(expected);

    [Fact]
    public void No_Title_Calls_Anything_A_Tax_Invoice() =>
        // The seller is not GST-registered; in Singapore that phrase is reserved for one who is.
        Enum.GetValues<DocumentType>().Select(DocumentWording.TitleFor)
            .Should().OnlyContain(title => !title.Contains("Tax", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void An_Unknown_Document_Type_Has_No_Title() =>
        // Better to throw than to print an untitled document or guess at "Invoice".
        ((Func<string>)(() => DocumentWording.TitleFor((DocumentType)99)))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Either_Alternative_Satisfies_A_Group() =>
        BuyerFieldRequirements.Missing(["email|phone"], Empty with { Phone = "+6598765432" })
            .Should().BeEmpty("the requirement is that the buyer is reachable, not how");

    [Fact]
    public void A_Group_With_No_Alternative_Present_Is_Reported_Whole() =>
        BuyerFieldRequirements.Missing(["email|phone"], Empty)
            .Should().BeEquivalentTo(["email|phone"],
                "the caller has to tell the buyer what would satisfy it");

    [Fact]
    public void Only_The_Unsatisfied_Groups_Are_Reported() =>
        BuyerFieldRequirements.Missing(
                ["email", "billingAddress"], Empty with { Email = "buyer@example.com" })
            .Should().BeEquivalentTo(["billingAddress"]);

    [Fact]
    public void Whitespace_Is_Not_A_Value() =>
        // " " passes a null check and prints as an empty box on the invoice.
        BuyerFieldRequirements.Missing(["billingAddress"], Empty with { BillingAddress = "   " })
            .Should().BeEquivalentTo(["billingAddress"]);

    [Fact]
    public void An_Unknown_Field_Name_Can_Never_Be_Satisfied() =>
        // A typo in configuration must refuse every document rather than waive a requirement.
        BuyerFieldRequirements.Missing(["emial"], Empty with { Email = "buyer@example.com" })
            .Should().BeEquivalentTo(["emial"]);

    [Fact]
    public void No_Requirements_Means_Nothing_Is_Missing() =>
        BuyerFieldRequirements.Missing([], Empty).Should().BeEmpty();

    [Fact]
    public void A_Non_Gst_Seller_Charges_Nothing_And_Says_So() =>
        new NonGstTaxPolicy().Assess(50_000, "USD")
            .Should().Be(new TaxAssessment(0, "Not GST registered"));

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(100_000_000L)]
    public void The_Tax_Statement_Does_Not_Depend_On_The_Amount(long subtotal) =>
        new NonGstTaxPolicy().Assess(subtotal, "SGD").TaxMinor.Should().Be(0);

    [Fact]
    public void A_Snapshot_Round_Trips_Through_Json()
    {
        var content = Sample();

        var json = JsonSerializer.Serialize(content, DocumentContentJson.Options);
        var restored = JsonSerializer.Deserialize<DocumentContent>(json, DocumentContentJson.Options);

        restored.Should().Be(content,
            "a stored invoice must come back as the invoice that was issued");
    }

    [Fact]
    public void Serializing_Twice_Gives_Identical_Bytes()
    {
        var content = Sample();

        var first = JsonSerializer.Serialize(content, DocumentContentJson.Options);
        var second = JsonSerializer.Serialize(content, DocumentContentJson.Options);

        second.Should().Be(first, "a stored document has to be hashable to be sealed");
    }

    [Fact]
    public void Equality_Compares_Lines_By_Value()
    {
        var content = Sample();
        var same = Sample();

        // The record's generated equality would compare the list by reference, which would make
        // every round-tripped snapshot unequal to itself.
        same.Should().Be(content);
        (same with { Lines = [.. content.Lines, content.Lines[0] with { LineNo = 2 }] })
            .Should().NotBe(content);
    }

    private static DocumentContent Sample() => new(
        Title: "Invoice",
        DocumentType: "Commercial",
        DocumentNumber: "INV-2026-000123",
        IssuedAt: new DateTimeOffset(2026, 11, 7, 9, 30, 0, TimeSpan.Zero),
        OrderNumber: "ORD-2026-000123",
        Channel: "Partner",
        ExternalOrderId: "PO-77",
        Seller: new SellerBlock("Demo Pte. Ltd.", "202612345A", "1 Example Road", "ar@example.com", "Thank you"),
        Buyer: new BuyerBlock(42, "C-77", "Pat Lim", "Lim Trading", "pat@example.com", null, "2 Buyer Street", null),
        Lines: [new DocumentLine(1, "SKU-1", "A kettle", 2, 1999, 3998)],
        Currency: "USD",
        SubtotalMinor: 3998,
        TaxMinor: 0,
        TotalMinor: 3998,
        TaxStatement: "Not GST registered",
        FulfillmentStatement: DocumentWording.FulfillmentStatement,
        PaymentStatus: "Paid",
        PaymentSource: "PartnerAccount",
        PaymentInstruction: null,
        RelatedDocumentNumber: null,
        RefundAmountMinor: null,
        RefundReason: null);
}
