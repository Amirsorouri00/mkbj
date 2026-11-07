using EwaysVNext.Modules.Sales.Application.Domain.Documents;

namespace EwaysVNext.Modules.Sales.Application.Ports.Outbound;

/// <summary>
/// Turns a stored document snapshot into a PDF.
/// </summary>
/// <remarks>
/// <b>Pure and deterministic.</b> The same snapshot must render byte-identical bytes every time,
/// because a reprint is meant to be the same document — not a new one that happens to say the
/// same things. Nothing may enter the output that is not in the snapshot: no clock, no current
/// seller details, no configuration.
/// </remarks>
public interface IDocumentPdfRenderer
{
    byte[] Render(DocumentContent content);
}

/// <summary>
/// Where rendered documents live.
/// </summary>
/// <remarks>
/// Deliberately not on the order-write path. An invoice is rendered the first time someone asks
/// for it, so object storage being down delays a download rather than refusing a sale.
/// </remarks>
public interface IDocumentStore
{
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task PutPdfAsync(string key, byte[] pdf, CancellationToken ct = default);

    /// <summary>A link that works for five minutes.</summary>
    /// <remarks>
    /// Short because an invoice is a private document and a URL is a bearer token: long enough
    /// to click, short enough that one pasted into a chat stops working before it spreads.
    /// </remarks>
    Task<Uri> GetDownloadUrlAsync(string key, CancellationToken ct = default);
}
