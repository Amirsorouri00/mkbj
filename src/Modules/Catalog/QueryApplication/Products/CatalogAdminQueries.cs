using System.Text.Json;
using Mkbj.Database;
using Mkbj.Modules.Catalog.Application.Products.Domain;
using Mkbj.Modules.Catalog.QueryApplication.Brands;
using Mkbj.Outcomes;
using Mkbj.Queries.Application.Paginations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Mkbj.Modules.Catalog.QueryApplication.Products;

public sealed class CatalogAdminQueries(MkbjDbContext db, IConfiguration configuration)
    : ICatalogAdminQueries
{
    private const int MaxPageSize = 100;

    /// <summary>
    /// Where a published image is served from. Read per query rather than cached so that
    /// repointing the CDN does not need a restart; it is a configuration string, not a hot path.
    /// </summary>
    private string MediaBaseUrl =>
        (configuration["Storefront:MediaBaseUrl"] ?? string.Empty).TrimEnd('/');

    public async Task<PaginatedQuery<BrandAdminRow>> ListBrandsAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);

        // Inactive brands are included on purpose: the admin list is where someone goes to turn
        // one back on, and hiding them would make that impossible.
        var brands = db.Brands.AsNoTracking();

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // Matched against aliases too, because "why can't I use this name?" is the question
            // this search exists to answer.
            brands = brands.Where(brand =>
                brand.DisplayName.Contains(term) ||
                brand.CanonicalName.Contains(term) ||
                brand.Aliases.Any(alias => alias.Alias.Contains(term)));
        }

        var totalCount = await brands.CountAsync(ct);

        var items = await brands
            .OrderBy(brand => brand.CanonicalName)
            .ThenBy(brand => brand.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(brand => new BrandAdminRow(
                brand.Id,
                brand.CanonicalName,
                brand.DisplayName,
                brand.Provenance,
                brand.IsActive,
                brand.Aliases.OrderBy(alias => alias.Alias).Select(alias => alias.Alias).ToList(),
                brand.CreatedAt,
                brand.UpdatedAt))
            .ToListAsync(ct);

        return Paginate(page, pageSize, totalCount, items);
    }

    public async Task<PaginatedQuery<ProductAdminRowDto>> ListProductsAsync(
        ProductAdminFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        (page, pageSize) = Paging(page, pageSize);

        var products = db.Products.AsNoTracking();

        var term = filter.Q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // MPN is in the search because an admin chasing a supplier's email has the part
            // number in front of them, not the title.
            products = products.Where(product =>
                product.Title.Contains(term) ||
                product.Slug.Contains(term) ||
                (product.Mpn != null && product.Mpn.Contains(term)));
        }

        if (filter.Status is { } status)
            products = products.Where(product => product.Status == status);

        if (filter.BrandId is { } brandId)
            products = products.Where(product => product.BrandId == brandId);

        if (filter.CategoryId is { } categoryId)
        {
            // Primary or secondary: an admin filtering by category means "show me what is filed
            // here", and the distinction is not one they are making.
            products = products.Where(product =>
                product.PrimaryCategoryId == categoryId ||
                db.CategoryProducts.Any(assignment =>
                    assignment.ProductId == product.Id && assignment.CategoryId == categoryId));
        }

        var totalCount = await products.CountAsync(ct);

        var items = await products
            .OrderByDescending(product => product.CreatedAt)
            .ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductAdminRowDto(
                product.Id,
                product.Title,
                product.Slug,
                product.Status.ToString(),
                product.DeactivatedByAdmin,
                product.BrandId,
                db.Brands.Where(brand => brand.Id == product.BrandId)
                    .Select(brand => brand.DisplayName).FirstOrDefault() ?? string.Empty,
                product.PrimaryCategoryId,
                db.Categories.Where(category => category.Id == product.PrimaryCategoryId)
                    .Select(category => category.Name).FirstOrDefault() ?? string.Empty,
                product.Mpn,
                product.Variants.Count,
                product.Variants.Count(variant => variant.IsActive),
                product.Media.Any(image => image.UsageRights == MediaUsageRights.Allowed),
                product.MergedIntoProductId,
                product.CreatedAt,
                product.UpdatedAt))
            .ToListAsync(ct);

        return Paginate(page, pageSize, totalCount, items);
    }

    public async Task<Result<ProductAdminDetailDto>> GetProductAsync(
        long productId, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking()
            .Include(candidate => candidate.Variants)
            .Include(candidate => candidate.Media)
            .Include(candidate => candidate.Identifiers)
            .FirstOrDefaultAsync(candidate => candidate.Id == productId, ct);

        if (product is null)
        {
            return Result.Failure<ProductAdminDetailDto>(
                new Error("PRODUCT_NOT_FOUND", "Product was not found.", 404));
        }

        var brandName = await db.Brands.AsNoTracking()
            .Where(brand => brand.Id == product.BrandId)
            .Select(brand => brand.DisplayName).FirstOrDefaultAsync(ct) ?? string.Empty;

        var categoryName = await db.Categories.AsNoTracking()
            .Where(category => category.Id == product.PrimaryCategoryId)
            .Select(category => category.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

        var secondaryCategoryIds = await db.CategoryProducts.AsNoTracking()
            .Where(assignment => assignment.ProductId == productId)
            .Select(assignment => assignment.CategoryId)
            .OrderBy(id => id)
            .ToListAsync(ct);

        var baseUrl = MediaBaseUrl;

        return Result.Success(new ProductAdminDetailDto(
            product.Id,
            product.Title,
            product.Slug,
            product.ShortDescription,
            product.Description,
            product.Status.ToString(),
            product.DeactivatedByAdmin,
            product.BrandId,
            brandName,
            product.PrimaryCategoryId,
            categoryName,
            secondaryCategoryIds,
            product.Mpn,
            product.MergedIntoProductId,
            product.SourceRefs,
            product.Variants
                .OrderBy(variant => variant.AttributeSignature, StringComparer.Ordinal)
                .ThenBy(variant => variant.Id)
                .Select(variant => new ProductAdminVariantDto(
                    variant.Id, variant.Sku.Value, variant.AttributeSignature,
                    variant.AttributeValues, variant.IsActive, variant.DeactivatedByAdmin))
                .ToList(),
            product.Identifiers
                .OrderBy(identifier => identifier.Type).ThenBy(identifier => identifier.Value, StringComparer.Ordinal)
                .Select(identifier => new ProductAdminIdentifierDto(
                    identifier.Id, identifier.Type.ToString(), identifier.Value, identifier.SourceDomain))
                .ToList(),
            product.Media
                .OrderBy(image => image.SortOrder).ThenBy(image => image.Id)
                .Select(image => new ProductAdminMediaDto(
                    image.Id, image.ObjectKey, $"{baseUrl}/{image.ObjectKey}", image.SortOrder,
                    image.Alt, image.UsageRights.ToString(), image.IsUsable))
                .ToList(),
            // Computed from the loaded aggregate rather than reproduced in SQL: one definition of
            // "ready to publish", and the admin screen cannot drift from the button.
            product.ActivationProblems(),
            product.CreatedAt,
            product.UpdatedAt));
    }

    public async Task<IReadOnlyList<ProductAdminVariantLookupDto>> SearchVariantsAsync(
        string query, int take, CancellationToken ct = default)
    {
        var term = query.Trim();
        if (term.Length < 2) return [];

        var limit = Math.Clamp(take, 1, 20);
        var hasExactId = long.TryParse(term, out var exactId);
        var pattern = $"%{EscapeLike(term)}%";

        // SKU is a value object in the aggregate. Reaching through Sku.Value in LINQ cannot be
        // translated by every relational provider, even though the persistence edge stores it as
        // text. This read-side query names that storage contract explicitly and keeps all user
        // input parameterized.
        FormattableString sql;
        if (hasExactId)
        {
            sql = $"""
              SELECT
                  variant.id AS variant_id,
                  product.id AS product_id,
                  product.title AS product_title,
                  brand.display_name AS brand_name,
                  category.name AS category_name,
                  variant.sku AS sku,
                  variant.attribute_values::text AS attribute_values_json,
                  variant.is_active AS is_active
              FROM catalog.variants AS variant
              JOIN catalog.products AS product ON product.id = variant.product_id
              JOIN catalog.brands AS brand ON brand.id = product.brand_id
              JOIN catalog.categories AS category ON category.id = product.primary_category_id
              WHERE product.merged_into_product_id IS NULL
                AND (
                      variant.id = {exactId}
                      OR product.id = {exactId}
                      OR variant.sku ILIKE {pattern} ESCAPE '\'
                      OR product.title ILIKE {pattern} ESCAPE '\'
                      OR COALESCE(product.mpn, '') ILIKE {pattern} ESCAPE '\'
                    )
              ORDER BY CASE WHEN variant.id = {exactId} THEN 0 ELSE 1 END, product.title, variant.sku
              LIMIT {limit}
              """;
        }
        else
        {
            sql = $"""
              SELECT
                  variant.id AS variant_id,
                  product.id AS product_id,
                  product.title AS product_title,
                  brand.display_name AS brand_name,
                  category.name AS category_name,
                  variant.sku AS sku,
                  variant.attribute_values::text AS attribute_values_json,
                  variant.is_active AS is_active
              FROM catalog.variants AS variant
              JOIN catalog.products AS product ON product.id = variant.product_id
              JOIN catalog.brands AS brand ON brand.id = product.brand_id
              JOIN catalog.categories AS category ON category.id = product.primary_category_id
              WHERE product.merged_into_product_id IS NULL
                AND (
                      variant.sku ILIKE {pattern} ESCAPE '\'
                      OR product.title ILIKE {pattern} ESCAPE '\'
                      OR COALESCE(product.mpn, '') ILIKE {pattern} ESCAPE '\'
                    )
              ORDER BY product.title, variant.sku
              LIMIT {limit}
              """;
        }

        var rows = await db.Database.SqlQuery<VariantLookupRow>(sql).ToListAsync(ct);

        return rows.Select(row => new ProductAdminVariantLookupDto(
            row.VariantId,
            row.ProductId,
            row.ProductTitle,
            row.BrandName,
            row.CategoryName,
            row.Sku,
            JsonSerializer.Deserialize<Dictionary<string, string>>(row.AttributeValuesJson) ?? [],
            row.IsActive)).ToList();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static (int Page, int PageSize) Paging(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));

    private static PaginatedQuery<T> Paginate<T>(int page, int pageSize, int totalCount, IReadOnlyList<T> items) =>
        new(page, pageSize, (int)Math.Ceiling(totalCount / (double)pageSize), totalCount, items);

    private sealed class VariantLookupRow
    {
        public long VariantId { get; init; }
        public long ProductId { get; init; }
        public string ProductTitle { get; init; } = string.Empty;
        public string BrandName { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
        public string Sku { get; init; } = string.Empty;
        public string AttributeValuesJson { get; init; } = "{}";
        public bool IsActive { get; init; }
    }
}
