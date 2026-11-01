using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Storage;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Media;

internal sealed class ObjectStorageCatalogMediaStore(IObjectStorage storage) : ICatalogMediaStore
{
    public Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default) =>
        storage.PutAsync(StorageBuckets.CatalogMedia, objectKey, content, contentType, ct);

    public Task CopyFromStagingAsync(string stagingKey, string objectKey, CancellationToken ct = default) =>
        storage.CopyAsync(StorageBuckets.ImportStaging, stagingKey, StorageBuckets.CatalogMedia, objectKey, ct);

    public Task DeleteAsync(string objectKey, CancellationToken ct = default) =>
        storage.DeleteAsync(StorageBuckets.CatalogMedia, objectKey, ct);
}
