namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;

/// <summary>
/// Where product images live. Narrower than the storage port on purpose: the catalogue names
/// objects, and which bucket they land in is the adapter's business, not the service's.
/// </summary>
public interface ICatalogMediaStore
{
    Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Promotes a file an import already downloaded into the published bucket, without pulling
    /// the bytes through this process.
    /// </summary>
    Task CopyFromStagingAsync(string stagingKey, string objectKey, CancellationToken ct = default);

    Task DeleteAsync(string objectKey, CancellationToken ct = default);
}
