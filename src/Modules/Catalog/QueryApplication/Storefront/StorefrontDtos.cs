namespace EwaysVNext.Modules.Catalog.QueryApplication.Storefront;

/// <summary>
/// An amount as a two-decimal string, never a number.
/// </summary>
/// <remarks>
/// A JSON number is a double to most clients, and a double does not hold money. The whole stack
/// stores minor units for the same reason; this is where they become text, once, instead of in
/// every consumer.
/// </remarks>
public sealed record MoneyDto(string Amount, string Currency);

public sealed record ProductCardDto(
    long ProductId,
    string Slug,
    string Title,
    long BrandId,
    string BrandName,
    long CategoryId,
    MoneyDto MinPrice,
    MoneyDto MaxPrice,
    int VariantCount,
    string? ImageUrl);

public sealed record CategoryTileDto(long CategoryId, string Name, string Slug, long ProductCount);

/// <remarks>
/// <c>BestSellers</c> is empty until the sales tables exist in phase 4. An empty section is
/// honest; a section ranked by a column that is always zero is not.
/// </remarks>
public sealed record HomeDto(
    string Currency,
    IReadOnlyList<ProductCardDto> NewArrivals,
    IReadOnlyList<ProductCardDto> BestSellers,
    IReadOnlyList<CategoryTileDto> FeaturedCategories);

public sealed record CategoryCrumbDto(long CategoryId, string Name, string Slug);

public sealed record VariantOfferDto(
    long VariantId, string Sku, IReadOnlyDictionary<string, string> Attributes, MoneyDto Price);

public sealed record ProductDetailDto(
    long ProductId,
    string Slug,
    string Title,
    string? ShortDescription,
    string? Description,
    long BrandId,
    string BrandName,
    IReadOnlyList<CategoryCrumbDto> Breadcrumb,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyList<VariantOfferDto> Variants,
    IReadOnlyDictionary<string, IReadOnlyList<string>> AttributeOptions);
