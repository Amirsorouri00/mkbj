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

/// <summary>
/// One listing request. Every filter is optional; the currency is not.
/// </summary>
/// <remarks>
/// <c>PriceMinMinor</c> and <c>PriceMaxMinor</c> are in the <b>requested</b> currency, because
/// that is the only price a visitor has seen. Converting their bounds back to a base currency
/// would filter on a number nobody was shown.
/// </remarks>
public sealed record ProductListQuery(
    string Currency,
    long? CategoryId,
    IReadOnlyList<long>? BrandIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Attributes,
    long? PriceMinMinor,
    long? PriceMaxMinor,
    string? Q,
    StorefrontSort? Sort,
    string? Cursor,
    int? Limit);

/// <remarks>
/// <c>UsedTypoFallback</c> is surfaced rather than hidden: a page that silently answered a
/// different question than the one asked is how "search is broken" reports start.
/// </remarks>
public sealed record ProductPageDto(
    IReadOnlyList<ProductCardDto> Items, string? NextCursor, bool UsedTypoFallback);

public sealed record FacetValueDto(string Value, string Label, long Count);

public sealed record FacetGroupDto(string Key, IReadOnlyList<FacetValueDto> Values);

/// <remarks><c>To</c> is null for the open-ended top bucket.</remarks>
public sealed record PriceBucketDto(MoneyDto From, MoneyDto? To, long Count);

public sealed record FacetSetDto(
    IReadOnlyList<FacetGroupDto> Attributes,
    IReadOnlyList<FacetValueDto> Brands,
    IReadOnlyList<PriceBucketDto> PriceBuckets);
