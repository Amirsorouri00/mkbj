using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Ports.Inbound;

/// <summary>Hands out a link to a document's PDF, rendering it on first request.</summary>
public interface IInvoiceService
{
    Task<Result<Uri>> GetPdfUrlAsync(
        DocumentAccess access, long documentId, CancellationToken ct = default);
}
