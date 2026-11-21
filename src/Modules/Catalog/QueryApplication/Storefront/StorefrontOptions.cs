namespace Mkbj.Modules.Catalog.QueryApplication.Storefront;

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

    /// <summary>How many categories the backend exposes to the homepage.</summary>
    public int FeaturedCategoryLimit { get; set; } = 8;

    /// <summary>How many newest products the backend exposes to the homepage.</summary>
    public int NewArrivalsLimit { get; set; } = 10;

    /// <summary>How many popular products the backend exposes to the homepage.</summary>
    public int BestSellersLimit { get; set; } = 10;

    /// <summary>How many category-specific product rails the homepage receives.</summary>
    public int CategorySectionLimit { get; set; } = 3;

    /// <summary>How many products are returned in each category-specific rail.</summary>
    public int ProductsPerCategorySection { get; set; } = 10;
}
