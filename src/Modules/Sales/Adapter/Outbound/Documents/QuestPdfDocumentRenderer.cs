using System.Globalization;
using Mkbj.Modules.Sales.Application.Domain.Documents;
using Mkbj.Modules.Sales.Application.Ports.Outbound;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Mkbj.Modules.Sales.Adapter.Outbound.Documents;

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
    private const string Producer = "MKBJ";
    private static readonly string[] ColumnLabels = ["#", "Product / SKU", "Qty", "Unit price", "Line total"];

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
                page.DefaultTextStyle(text => text.FontFamily("Lato").FontSize(10).FontColor("#181818"));

                page.Header().Element(header => Seller(header, content));
                page.Content().Element(body => Body(body, content));
                page.Footer().BorderTop(1).BorderColor("#DDDDDD").PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Text(content.Seller.Footer).FontSize(8).FontColor("#606060");
                    row.ConstantItem(75).AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(8).FontColor("#606060"));
                        text.Span("Page "); text.CurrentPageNumber(); text.Span(" of "); text.TotalPages();
                    });
                });
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
        container.BorderBottom(3).BorderColor("#F28C00").PaddingBottom(16).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Spacing(4);
                column.Item().Text("MKBJ").Bold().FontSize(24);
                column.Item().Text(content.Seller.LegalName).SemiBold();
                column.Item().Text($"UEN: {content.Seller.Uen}").FontSize(9);
                column.Item().Text(content.Seller.Address).FontSize(9);
                column.Item().Text(content.Seller.Email).FontSize(9);
            });
            row.ConstantItem(195).AlignRight().Column(column =>
            {
                column.Spacing(6);
                column.Item().AlignRight().Text(content.Title).Bold().FontSize(23);
                column.Item().AlignRight().Text(content.DocumentNumber).SemiBold();
                column.Item().AlignRight().Text("Issue date: " + content.IssuedAt.ToOffset(TimeSpan.FromHours(8)).ToString("dd MMM yyyy", CultureInfo.InvariantCulture)).FontSize(9);
                column.Item().AlignRight().Text("Singapore (UTC+08:00)").FontSize(8).FontColor("#606060");
            });
        });

    private static void Body(IContainer container, DocumentContent content) =>
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Spacing(12);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(bill =>
                {
                    bill.Item().PaddingBottom(6).Text("BILL TO").Bold().FontSize(9).FontColor("#606060");
                    bill.Item().Element(buyer => Buyer(buyer, content.Buyer));
                });
                row.ConstantItem(195).Column(reference =>
                {
                    reference.Spacing(5);
                    reference.Item().Text($"Order: {content.OrderNumber}").FontSize(9);
                    if (!string.IsNullOrEmpty(content.ExternalOrderId))
                        reference.Item().Text($"Your reference: {content.ExternalOrderId}").FontSize(9);
                    reference.Item().Text($"Payment: {content.PaymentStatus}").SemiBold().FontSize(9);
                    if (!string.IsNullOrEmpty(content.PaymentSource))
                        reference.Item().Text($"Source: {content.PaymentSource}").FontSize(9);
                });
            });
            column.Item().Element(lines => Lines(lines, content));
            column.Item().Element(totals => Totals(totals, content));

            // Said on every document, because the system knows what an operator marked as
            // fulfilled and not what arrived.
            column.Item().Text(content.FulfillmentStatement).FontSize(9).FontColor("#606060");

            if (content.PaymentInstruction is { } instruction)
                column.Item().Text(instruction.Text);

            if (content.RelatedDocumentNumber is { } related)
                column.Item().Text($"Related document: {related}");

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
                columns.ConstantColumn(35);
                columns.RelativeColumn();
                columns.ConstantColumn(40);
                columns.ConstantColumn(85);
                columns.ConstantColumn(85);
            });

            table.Header(header =>
            {
                for (var index = 0; index < ColumnLabels.Length; index++)
                {
                    var cell = header.Cell().Background("#1C1C1C").Padding(8);
                    (index >= 2 ? cell.AlignRight() : cell).Text(ColumnLabels[index]).FontSize(9).SemiBold().FontColor(Colors.White);
                }
            });

            foreach (var line in content.Lines)
            {
                IContainer Cell(IContainer cell) => cell.ShowEntire().Background(line.LineNo % 2 == 0 ? "#F5F5F3" : "#FFFFFF")
                    .BorderBottom(0.5f).BorderColor("#DDDDDD").PaddingVertical(10).PaddingHorizontal(8);
                table.Cell().Element(Cell).Text(Text(line.LineNo)).FontSize(9);
                table.Cell().Element(Cell).Column(description =>
                {
                    description.Item().Text(line.Description).SemiBold();
                    description.Item().PaddingTop(4).Text(line.Sku).FontSize(8).FontColor("#606060");
                });
                table.Cell().Element(Cell).AlignRight().Text(Text(line.Quantity)).FontSize(9);
                table.Cell().Element(Cell).AlignRight().Text(Money(line.UnitPriceMinor, content.Currency)).FontSize(9);
                table.Cell().Element(Cell).AlignRight().Text(Money(line.LineTotalMinor, content.Currency)).FontSize(9);
            }
        });

    private static void Totals(IContainer container, DocumentContent content) =>
        container.ShowEntire().Column(column =>
        {
            column.Spacing(7);
            void Amount(string label, long amount, bool total = false) =>
                column.Item().AlignRight().Width(270).Background(total ? "#FFF2DC" : "#FFFFFF").Padding(8).Row(row =>
                {
                    row.RelativeItem().Text(label).SemiBold();
                    row.RelativeItem().AlignRight().Text(Money(amount, content.Currency)).Bold();
                });
            Amount("Subtotal", content.SubtotalMinor);
            Amount("Tax", content.TaxMinor);
            Amount("Total", content.TotalMinor, true);

            // Without this a buyer may read a zero tax line as a zero-rated supply and try to
            // reclaim input tax that was never charged.
            column.Item().AlignRight().Text(content.TaxStatement).FontSize(9).FontColor("#606060");
        });

    private static string Money(long minor, string currency) =>
        $"{currency.Trim()} {Mkbj.Modules.Sales.Application.Money.AmountText.Format(minor)}";

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
