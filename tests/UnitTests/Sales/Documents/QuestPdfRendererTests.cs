using System.Text.RegularExpressions;
using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Adapter.Outbound.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Documents;

/// <summary>
/// The PDF a buyer actually receives.
/// </summary>
/// <remarks>
/// Read back with a PDF parser rather than asserted on the renderer's calls, because what
/// matters is what ends up on the page. A test that checked the layout API would pass while the
/// seller's UEN was missing from the document.
/// </remarks>
public sealed class QuestPdfRendererTests
{
    private static readonly QuestPdfDocumentRenderer Renderer = new();

    [Fact]
    public void Rendering_The_Same_Snapshot_Twice_Gives_The_Same_Bytes()
    {
        var content = Sample();

        // A reprint is meant to be the same document, not a new one that says the same things.
        // Byte equality is what lets "is this the invoice we sent?" have an answer.
        Renderer.Render(content).Should().Equal(Renderer.Render(content));
    }

    [Fact]
    public void The_Invoice_Carries_Everything_It_Must()
    {
        var text = TextOf(Sample());

        text.Should().Contain("Invoice");
        text.Should().Contain("Demo Pte. Ltd.");
        text.Should().Contain("202612345A", "the seller's registration number identifies them");
        text.Should().Contain("INV-000123");
        text.Should().Contain("Not GST registered");
        text.Should().Contain(DocumentWording.FulfillmentStatement);
        text.Should().Contain("SKU-1");
        text.Should().Contain("500.00");
        text.Should().Contain("Pat Lim");
    }

    [Fact]
    public void No_Document_Calls_Itself_A_Tax_Invoice()
    {
        foreach (var type in Enum.GetValues<DocumentType>())
        {
            var text = TextOf(Sample() with
            {
                Title = DocumentWording.TitleFor(type),
                DocumentType = type.ToString(),
            });

            // The seller is not GST-registered; in Singapore the phrase is reserved for one who
            // is. The source scan cannot see a string built at render time, so this checks the
            // rendered page.
            Regex.IsMatch(text, "tax invoice", RegexOptions.IgnoreCase)
                .Should().BeFalse($"a {type} must not read as a GST document");
        }
    }

    [Fact]
    public void A_Pro_Forma_Tells_The_Buyer_How_To_Pay()
    {
        var text = TextOf(Sample() with
        {
            Title = DocumentWording.TitleFor(DocumentType.ProForma),
            DocumentType = nameof(DocumentType.ProForma),
            PaymentStatus = "Unpaid",
            PaymentInstruction = new PaymentInstructionBlock(9, 3, "Transfer to DBS 123-456-789"),
        });

        text.Should().Contain("Pro Forma Invoice");
        text.Should().Contain("Transfer to DBS 123-456-789",
            "a pro forma whose purpose is to be paid must say where");
    }

    [Fact]
    public void A_Credit_Note_Points_At_The_Invoice_It_Credits()
    {
        var text = TextOf(Sample() with
        {
            Title = DocumentWording.TitleFor(DocumentType.CreditNote),
            DocumentType = nameof(DocumentType.CreditNote),
            RelatedDocumentNumber = "INV-000123",
            RefundAmountMinor = 50_000,
            RefundReason = "Returned undamaged",
        });

        text.Should().Contain("Credit Note");
        text.Should().Contain("INV-000123", "a credit note against nothing is unexplainable");
        text.Should().Contain("Returned undamaged");
    }

    [Fact]
    public void A_Buyer_With_Only_A_Phone_Renders_Without_Blank_Lines()
    {
        var text = TextOf(Sample() with
        {
            Buyer = new BuyerBlock(42, "C-77", null, null, null, "+6598765432", null, null),
        });

        text.Should().Contain("+6598765432");
        text.Should().NotContain("  ,", "an absent field is omitted, not printed as empty");
    }

    [Fact]
    public void Every_Line_Appears()
    {
        var content = Sample() with
        {
            Lines =
            [
                new DocumentLine(1, "SKU-1", "A kettle", 2, 1_999, 3_998),
                new DocumentLine(2, "SKU-2", "A toaster", 1, 46_002, 46_002),
            ],
        };

        var text = TextOf(content);

        text.Should().Contain("SKU-2");
        text.Should().Contain("A toaster");
    }

    private static string TextOf(DocumentContent content)
    {
        using var document = PdfDocument.Open(Renderer.Render(content));

        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    private static DocumentContent Sample() => new(
        Title: "Invoice",
        DocumentType: "Commercial",
        DocumentNumber: "INV-000123",
        IssuedAt: new DateTimeOffset(2026, 11, 7, 9, 30, 0, TimeSpan.Zero),
        OrderNumber: "ORD-000123",
        Channel: "PartnerApi",
        ExternalOrderId: "PO-77",
        Seller: new SellerBlock(
            "Demo Pte. Ltd.", "202612345A", "1 Example Road", "ar@example.com", "Thank you"),
        Buyer: new BuyerBlock(
            42, "C-77", "Pat Lim", "Lim Trading", "pat@example.com", null, "2 Buyer Street", null),
        Lines: [new DocumentLine(1, "SKU-1", "A kettle", 1, 50_000, 50_000)],
        Currency: "USD",
        SubtotalMinor: 50_000,
        TaxMinor: 0,
        TotalMinor: 50_000,
        TaxStatement: DocumentWording.NotGstRegistered,
        FulfillmentStatement: DocumentWording.FulfillmentStatement,
        PaymentStatus: "Paid",
        PaymentSource: "PartnerReported",
        PaymentInstruction: null,
        RelatedDocumentNumber: null,
        RefundAmountMinor: null,
        RefundReason: null);
}
