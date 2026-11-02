using System.Globalization;
using System.Text.Json;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Storefront;

/// <summary>
/// Reads the storefront views and shapes them for a browsing visitor.
/// </summary>
/// <remarks>
/// Read-only by design (ADR-0016): no aggregate is loaded, no rule is applied, nothing is
/// written. The only judgement here is whether the currency is one the seller sells in — which
/// is a fact about the request, not about any product.
/// </remarks>
public sealed class StorefrontQueries(
    EwaysVNextDbContext db,
    ICategoryQueries categories,
    IOptions<StorefrontOptions> options) : IStorefrontQueries
{
    private const int MaxFeaturedCategories = 8;

    private static readonly Error CurrencyNotEnabled = new(
        "CURRENCY_NOT_ENABLED", "The shop does not sell in that currency.", 422);

    private static readonly Error NotFound = new(
        "NOT_FOUND", "No such product.", 404);

    public async Task<Result<HomeDto>> GetHomeAsync(string currency, CancellationToken ct = default)
    {
        var code = Normalize(currency);
        if (!await IsEnabledAsync(code, ct)) return Result.Failure<HomeDto>(CurrencyNotEnabled);

        var products = await db.Database
            .SqlQueryRaw<HomeProductRow>(StorefrontSql.HomeProducts, Text("currency", code))
            .ToListAsync(ct);

        var tiles = await db.Database
            .SqlQueryRaw<HomeCategoryRow>(StorefrontSql.HomeCategories, Text("currency", code))
            .ToListAsync(ct);

        return Result.Success(new HomeDto(
            code,
            [.. products.Where(row => row.Section == "new_arrivals").Select(row => Card(row, code))],
            [.. products.Where(row => row.Section == "best_sellers").Select(row => Card(row, code))],
            [.. tiles.Take(MaxFeaturedCategories)
                .Select(row => new CategoryTileDto(row.CategoryId, row.Name, row.Slug, row.ProductCount))]));
    }

    public Task<IReadOnlyList<CategoryTreeNodeDto>> GetCategoryTreeAsync(CancellationToken ct = default) =>
        categories.GetTreeAsync(ct);

    public async Task<Result<ProductDetailDto>> GetProductBySlugAsync(
        string slug, string currency, CancellationToken ct = default)
    {
        var code = Normalize(currency);
        if (!await IsEnabledAsync(code, ct)) return Result.Failure<ProductDetailDto>(CurrencyNotEnabled);

        var product = await db.Database
            .SqlQueryRaw<ProductDetailRow>(
                StorefrontSql.ProductBySlug, Text("slug", slug ?? string.Empty), Text("currency", code))
            .FirstOrDefaultAsync(ct);

        // Absent from the view is absent from the shop, whatever the live table holds: a Draft
        // product, a deactivated one and a misspelled slug are the same answer to a visitor.
        if (product is null) return Result.Failure<ProductDetailDto>(NotFound);

        var breadcrumb = await db.Database
            .SqlQueryRaw<CrumbRow>(
                StorefrontSql.Breadcrumb,
                new NpgsqlParameter("ids", product.AncestorCategoryIds))
            .ToListAsync(ct);

        var offers = await db.Database
            .SqlQueryRaw<VariantOfferRow>(
                StorefrontSql.VariantOffers,
                new NpgsqlParameter("productId", product.ProductId), Text("currency", code))
            .ToListAsync(ct);

        var variants = offers
            .Select(row => new VariantOfferDto(
                row.VariantId, row.Sku, Attributes(row.AttributeValues), Money(row.PriceMinor, code)))
            .ToList();

        return Result.Success(new ProductDetailDto(
            product.ProductId,
            product.Slug,
            product.Title,
            product.ShortDescription,
            product.Description,
            product.BrandId,
            product.BrandName,
            [.. breadcrumb.Select(row => new CategoryCrumbDto(row.CategoryId, row.Name, row.Slug))],
            [.. product.ImageKeys.Select(Url)!],
            variants,
            AttributeOptions(variants)));
    }

    /// <summary>
    /// The distinct values each attribute takes across the product's offers, sorted — what a
    /// selector on the page offers.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> AttributeOptions(
        IReadOnlyList<VariantOfferDto> variants) =>
        variants
            .SelectMany(variant => variant.Attributes)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)[.. group.Select(pair => pair.Value)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                StringComparer.Ordinal);

    private async Task<bool> IsEnabledAsync(string currency, CancellationToken ct) =>
        await db.Database
            .SqlQueryRaw<bool>(StorefrontSql.CurrencyIsEnabled, Text("currency", currency))
            .SingleAsync(ct);

    private ProductCardDto Card(HomeProductRow row, string currency) => new(
        row.ProductId, row.Slug, row.Title, row.BrandId, row.BrandName, row.CategoryId,
        Money(row.MinPriceMinor, currency), Money(row.MaxPriceMinor, currency),
        row.VariantCount, Url(row.ImageKey));

    /// <summary>Minor units to a two-decimal string, invariantly.</summary>
    private static MoneyDto Money(long minor, string currency) => new(
        (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture), currency);

    private string? Url(string? objectKey) =>
        objectKey is null ? null : options.Value.MediaBaseUrl + objectKey;

    private static Dictionary<string, string> Attributes(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    /// <summary>
    /// Currency codes are <c>char(3)</c>, so a value read back carries padding and a value sent
    /// in may carry whitespace. Comparing them anywhere else means comparing padding.
    /// </summary>
    private static string Normalize(string? currency) =>
        (currency ?? string.Empty).Trim().ToUpperInvariant();

    private static NpgsqlParameter Text(string name, string value) => new(name, value);

    private sealed class HomeProductRow
    {
        public string Section { get; init; } = string.Empty;
        public long ProductId { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public long BrandId { get; init; }
        public string BrandName { get; init; } = string.Empty;
        public long CategoryId { get; init; }
        public long MinPriceMinor { get; init; }
        public long MaxPriceMinor { get; init; }
        public int VariantCount { get; init; }
        public string? ImageKey { get; init; }
    }

    private sealed class HomeCategoryRow
    {
        public long CategoryId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public long ProductCount { get; init; }
    }

    private sealed class ProductDetailRow
    {
        public long ProductId { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public long BrandId { get; init; }
        public string BrandName { get; init; } = string.Empty;
        public long[] AncestorCategoryIds { get; init; } = [];
        public string[] ImageKeys { get; init; } = [];
        public string? ShortDescription { get; init; }
        public string? Description { get; init; }
    }

    private sealed class CrumbRow
    {
        public long CategoryId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
    }

    private sealed class VariantOfferRow
    {
        public long VariantId { get; init; }
        public string Sku { get; init; } = string.Empty;
        public long PriceMinor { get; init; }
        public string AttributeValues { get; init; } = "{}";
    }
}
