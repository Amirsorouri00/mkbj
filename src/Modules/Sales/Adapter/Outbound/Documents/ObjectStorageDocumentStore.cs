using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Storage;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound.Documents;

/// <summary>
/// Documents in the private <c>documents</c> bucket.
/// </summary>
/// <remarks>
/// A separate bucket from catalogue media because the contents are different in kind: product
/// images are public by design, and an invoice names a buyer, an address and what they paid.
/// </remarks>
internal sealed class ObjectStorageDocumentStore(IObjectStorage storage) : IDocumentStore
{
    /// <summary>
    /// Five minutes.
    /// </summary>
    /// <remarks>
    /// A presigned URL is a bearer token for a private document: long enough to click, short
    /// enough that one pasted into a chat thread stops working before it travels.
    /// </remarks>
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    private const string ContentType = "application/pdf";

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        storage.ExistsAsync(StorageBuckets.Documents, key, ct);

    public async Task PutPdfAsync(string key, byte[] pdf, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        using var content = new MemoryStream(pdf, writable: false);
        await storage.PutAsync(StorageBuckets.Documents, key, content, ContentType, ct);
    }

    public Task<Uri> GetDownloadUrlAsync(string key, CancellationToken ct = default) =>
        storage.GetPresignedUrlAsync(StorageBuckets.Documents, key, LinkLifetime, ct);
}
