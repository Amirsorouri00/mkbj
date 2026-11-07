using System.Globalization;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound.Documents;

/// <summary>
/// Renders a document snapshot to a PDF.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every string on the page comes from the snapshot or from <see cref="DocumentWording"/>.</b>
/// Nothing is read from settings, configuration or the clock at render time — otherwise
/// reprinting a 2026 invoice in 2029 would quietly produce a 2029 document, and the one thing an
/// invoice must do is stay the invoice that was issued.
/// </para>
/// <para>
/// The PDF metadata dates are the document's own issue date rather than now, so the bytes are
/// identical on every render. A reprint that differed byte for byte could not be checksummed,
/// and "is this the document we sent?" would stop having an answer.
/// </para>
/// </remarks>
internal sealed class QuestPdfDocumentRenderer : IDocumentPdfRenderer
{
    private const string Producer = "EwaysVNext";

    static QuestPdfDocumentRenderer() =>
        // The community licence covers this use. Set once, here, rather than at startup: the
        // renderer is the only thing in the system that needs QuestPDF configured at all.
        QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(DocumentContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Document.Create(container => container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(text => text.FontSize(10));

                page.Header().Element(header => Seller(header, content));
                page.Content().Element(body => Body(body, content));
                page.Footer().Text(content.Seller.Footer);
            }))
            .WithMetadata(new DocumentMetadata
            {
                Title = $"{content.Title} {content.DocumentNumber}",
                Author = content.Seller.LegalName,
                Producer = Producer,
                Creator = Producer,
                CreationDate = content.IssuedAt,
                ModifiedDate = content.IssuedAt,
            })
            .GeneratePdf();
    }

    private static void Seller(IContainer container, DocumentContent content) =>
        container.Column(column =>
        {
            column.Item().Text(content.Seller.LegalName).Bold().FontSize(14);
            column.Item().Text(content.Seller.Uen);
            column.Item().Text(content.Seller.Address);
            column.Item().Text(content.Seller.Email);
        });

    private static void Body(IContainer container, DocumentContent content) =>
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Spacing(12);

            column.Item().Text(content.Title).Bold().FontSize(18);

            column.Item().Text(content.DocumentNumber);
            column.Item().Text(content.IssuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            column.Item().Text(content.OrderNumber);

            column.Item().Element(buyer => Buyer(buyer, content.Buyer));
            column.Item().Element(lines => Lines(lines, content));
            column.Item().Element(totals => Totals(totals, content));

            // Said on every document, because the system knows what an operator marked as
            // fulfilled and not what arrived.
            column.Item().Text(content.FulfillmentStatement);

            if (content.PaymentInstruction is { } instruction)
                column.Item().Text(instruction.Text);

            if (content.RelatedDocumentNumber is { } related)
                column.Item().Text(related);

            if (content.RefundReason is { } reason) column.Item().Text(reason);
        });

    private static void Buyer(IContainer container, BuyerBlock buyer) =>
        container.Column(column =>
        {
            foreach (var line in new[]
            {
                buyer.CompanyName,
                buyer.FullName,
                buyer.BillingAddress,
                buyer.Email,
                buyer.Phone,
                buyer.RegistrationNumber,
            })
            {
                if (!string.IsNullOrWhiteSpace(line)) column.Item().Text(line);
            }
        });

    private static void Lines(IContainer container, DocumentContent content) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(30);
                columns.RelativeColumn(2);
                columns.RelativeColumn(4);
                columns.ConstantColumn(50);
                columns.ConstantColumn(80);
                columns.ConstantColumn(80);
            });

            foreach (var line in content.Lines)
            {
                table.Cell().Text(Text(line.LineNo));
                table.Cell().Text(line.Sku);
                table.Cell().Text(line.Description);
                table.Cell().AlignRight().Text(Text(line.Quantity));
                table.Cell().AlignRight().Text(Money(line.UnitPriceMinor, content.Currency));
                table.Cell().AlignRight().Text(Money(line.LineTotalMinor, content.Currency));
            }
        });

    private static void Totals(IContainer container, DocumentContent content) =>
        container.AlignRight().Column(column =>
        {
            column.Item().Text(Money(content.SubtotalMinor, content.Currency));
            column.Item().Text(Money(content.TaxMinor, content.Currency));
            column.Item().Text(Money(content.TotalMinor, content.Currency)).Bold();

            // Without this a buyer may read a zero tax line as a zero-rated supply and try to
            // reclaim input tax that was never charged.
            column.Item().Text(content.TaxStatement);
        });

    private static string Money(long minor, string currency) =>
        $"{currency.Trim()} {EwaysVNext.Modules.Sales.Application.Money.AmountText.Format(minor)}";

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
