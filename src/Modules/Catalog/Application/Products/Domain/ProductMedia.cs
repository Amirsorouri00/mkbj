using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

/// <summary>
/// One image of a product, held as an object key in storage rather than as bytes or a URL — the
/// bucket and the signing of a public URL belong to the storage adapter, not to the catalogue.
/// </summary>
public sealed class ProductMedia : Entity<long>
{
    public long ProductId { get; private set; }

    public string ObjectKey { get; private set; } = null!;

    public int SortOrder { get; private set; }

    public string? Alt { get; private set; }

    /// <summary>Where the image was crawled from, when it was. Null for an admin upload.</summary>
    public string? SourceUrl { get; private set; }

    public MediaUsageRights UsageRights { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private ProductMedia() { }

    internal ProductMedia(long id, long productId, string objectKey, int sortOrder, string? alt,
        string? sourceUrl, MediaUsageRights usageRights, DateTimeOffset now)
    {
        Id = id;
        ProductId = productId;
        ObjectKey = objectKey;
        SortOrder = sortOrder;
        Alt = alt;
        SourceUrl = sourceUrl;
        UsageRights = usageRights;
        CreatedAt = now;
    }

    /// <summary>Usable on a storefront: shown only where the seller is known to be allowed to.</summary>
    public bool IsUsable => UsageRights == MediaUsageRights.Allowed;
}
