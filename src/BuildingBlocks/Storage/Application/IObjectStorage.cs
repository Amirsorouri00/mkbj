namespace EwaysVNext.Storage;

/// <summary>
/// Files that do not belong in the database: product images, import downloads, invoice PDFs.
/// </summary>
public interface IObjectStorage
{
    Task PutAsync(string bucket, string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>The caller disposes the stream. Returns null when the object does not exist.</summary>
    Task<Stream?> GetAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>
    /// A time-limited URL for a private object. The TTL must be between one second and seven days;
    /// outside that range it throws rather than quietly clamping, because a link that outlives its
    /// intent is a disclosure and one that expires on creation is a support call.
    /// </summary>
    Task<Uri> GetPresignedUrlAsync(string bucket, string key, TimeSpan ttl, CancellationToken ct = default);

    Task CopyAsync(string srcBucket, string srcKey, string dstBucket, string dstKey, CancellationToken ct = default);

    Task DeleteAsync(string bucket, string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default);
}
