using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Storage.Implementation;

/// <summary>
/// <see cref="IObjectStorage"/> over the S3 API. MinIO is the deployed implementation, but nothing
/// here is MinIO-specific — any S3-compatible store works.
/// </summary>
public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private static readonly TimeSpan MinimumTtl = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumTtl = TimeSpan.FromDays(7);

    private readonly IAmazonS3 _client;

    /// <summary>
    /// A second client bound to the public host. Signing is a local computation, so this client
    /// never connects — it exists only so the signature matches the host the recipient will use.
    /// </summary>
    private readonly IAmazonS3 _presigningClient;

    /// <summary>The scheme the recipient will use, taken from the configured public endpoint.</summary>
    private readonly string _publicScheme;

    public S3ObjectStorage(IOptions<MinioOptions> options)
    {
        var settings = options.Value;
        var publicEndpoint = string.IsNullOrWhiteSpace(settings.PublicEndpoint)
            ? settings.Endpoint
            : settings.PublicEndpoint;
        _publicScheme = new Uri(publicEndpoint).Scheme;

        _client = CreateClient(settings, settings.Endpoint);
        _presigningClient = string.IsNullOrWhiteSpace(settings.PublicEndpoint)
            || settings.PublicEndpoint == settings.Endpoint
                ? _client
                : CreateClient(settings, settings.PublicEndpoint);
    }

    public async Task PutAsync(
        string bucket, string key, Stream content, string contentType, CancellationToken ct = default) =>
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            // The SDK would otherwise buffer the whole payload to compute a checksum.
            AutoCloseStream = false,
        }, ct);

    public async Task<Stream?> GetAsync(string bucket, string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception error) when (IsMissing(error))
        {
            return null;
        }
    }

    public Task<Uri> GetPresignedUrlAsync(
        string bucket, string key, TimeSpan ttl, CancellationToken ct = default)
    {
        if (ttl < MinimumTtl || ttl > MaximumTtl)
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl,
                $"A presigned URL must live between {MinimumTtl} and {MaximumTtl}.");

        var url = _presigningClient.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(ttl),
        });

        return Task.FromResult(WithConfiguredScheme(url));
    }

    /// <summary>
    /// The SDK's endpoint rules emit an https presigned URL even when the configured endpoint is
    /// plain http, which is the normal case for a self-hosted store inside a compose network — the
    /// link then fails certificate validation at the recipient. The signature covers the host,
    /// path and query but not the scheme, so restoring the configured scheme leaves it valid.
    /// </summary>
    private Uri WithConfiguredScheme(string url)
    {
        var generated = new Uri(url);
        return generated.Scheme == _publicScheme
            ? generated
            : new UriBuilder(generated) { Scheme = _publicScheme, Port = generated.Port }.Uri;
    }

    public async Task CopyAsync(
        string srcBucket, string srcKey, string dstBucket, string dstKey, CancellationToken ct = default) =>
        await _client.CopyObjectAsync(srcBucket, srcKey, dstBucket, dstKey, ct);

    public async Task DeleteAsync(string bucket, string key, CancellationToken ct = default) =>
        await _client.DeleteObjectAsync(bucket, key, ct);

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception error) when (IsMissing(error))
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!ReferenceEquals(_presigningClient, _client)) _presigningClient.Dispose();
        _client.Dispose();
    }

    // A HEAD request answers 404 with no error code, so the status has to be checked too.
    private static bool IsMissing(AmazonS3Exception error) =>
        error.StatusCode == HttpStatusCode.NotFound
        || string.Equals(error.ErrorCode, "NoSuchKey", StringComparison.Ordinal);

    private static AmazonS3Client CreateClient(MinioOptions settings, string serviceUrl) =>
        new(settings.AccessKey, settings.SecretKey, new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            // Bucket names in the path, not the hostname: a self-hosted store has no per-bucket DNS.
            ForcePathStyle = true,
            AuthenticationRegion = settings.Region,
            // Without this the SDK signs presigned URLs as https even when ServiceURL is http,
            // and the link fails certificate validation at the client. MinIO inside the compose
            // network is plain http, so this is the normal case, not an edge one.
            UseHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
        });
}
