namespace EwaysVNext.Storage.Implementation;

/// <summary>
/// Connection settings for the S3-compatible object store (MinIO in production).
/// </summary>
public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    /// <summary>The address the application reaches the store on — inside the compose network.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// The address a browser reaches it on. Presigned URLs are signed for this host, which is
    /// usually not the one the API itself uses: a link signed for an internal container name is
    /// useless to the customer who receives it. Defaults to <see cref="Endpoint"/>.
    /// </summary>
    public string? PublicEndpoint { get; set; }

    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
}
