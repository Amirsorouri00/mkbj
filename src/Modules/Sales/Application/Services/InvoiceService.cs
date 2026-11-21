using Mkbj.Modules.Sales.Application.Ports.Inbound;
using Mkbj.Modules.Sales.Application.Ports.Outbound;
using Mkbj.Outcomes;

namespace Mkbj.Modules.Sales.Application.Services;

/// <summary>
/// Renders a document's PDF the first time it is asked for, and serves it from storage after.
/// </summary>
/// <remarks>
/// <para>
/// Lazily on purpose. Rendering during order creation would put a PDF library and an object
/// store on the path of every sale — two more things that can be slow or down between a partner
/// paying and an order existing. Most invoices are never downloaded at all.
/// </para>
/// <para>
/// The key is the one the aggregate recorded, so the file an order points at is the file that
/// gets written. Deriving it here would be a second opinion about where a document lives.
/// </para>
/// </remarks>
internal sealed class InvoiceService(
    IOrderRepository orders,
    IDocumentPdfRenderer renderer,
    IDocumentStore store) : IInvoiceService
{
    public async Task<Result<InvoicePdf>> GetPdfAsync(
        DocumentAccess access, long documentId, CancellationToken ct = default)
    {
        var document = await orders.FindDocumentAsync(documentId, access, ct);
        if (document is null) return Result.Failure<InvoicePdf>(SalesErrors.NotFound);
        var pdf = await store.ReadPdfAsync(document.PdfObjectKey, ct);
        if (pdf is null)
        {
            pdf = renderer.Render(document.Content);
            await store.PutPdfAsync(document.PdfObjectKey, pdf, ct);
        }
        return Result.Success(new InvoicePdf(pdf, $"{document.Content.DocumentNumber}.pdf"));
    }

    public async Task<Result<Uri>> GetPdfUrlAsync(
        DocumentAccess access, long documentId, CancellationToken ct = default)
    {
        // Scoped by the caller's identity in the query. A partner asking for another partner's
        // invoice gets nothing back, and cannot tell that from an id that never existed.
        var document = await orders.FindDocumentAsync(documentId, access, ct);
        if (document is null) return Result.Failure<Uri>(SalesErrors.NotFound);

        if (!await store.ExistsAsync(document.PdfObjectKey, ct))
        {
            await store.PutPdfAsync(
                document.PdfObjectKey, renderer.Render(document.Content), ct);
        }

        return Result.Success(await store.GetDownloadUrlAsync(document.PdfObjectKey, ct));
    }
}
