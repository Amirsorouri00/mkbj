using Mkbj.Modules.Sales.Application.Ports.Outbound;
using Mkbj.Outcomes;

namespace Mkbj.Modules.Sales.Application.Ports.Inbound;

public sealed record InvoicePdf(byte[] Content, string FileName);

/// <summary>Hands out a link to a document's PDF, rendering it on first request.</summary>
public interface IInvoiceService
{
    Task<Result<InvoicePdf>> GetPdfAsync(DocumentAccess access, long documentId, CancellationToken ct = default);
    Task<Result<Uri>> GetPdfUrlAsync(
        DocumentAccess access, long documentId, CancellationToken ct = default);
}
