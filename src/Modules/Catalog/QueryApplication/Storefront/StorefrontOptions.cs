namespace EwaysVNext.Modules.Catalog.QueryApplication.Storefront;

/// <summary>
/// How a stored object key becomes a URL a browser can fetch.
/// </summary>
/// <remarks>
/// Configuration rather than a constant: the same image is served from a CDN in production and
/// from the API in development, and neither belongs in compiled code.
/// </remarks>
public sealed class StorefrontOptions
{
    public const string SectionName = "Storefront";

    public string MediaBaseUrl { get; set; } = "/media/";
}
