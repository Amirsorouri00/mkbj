using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Services;

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
