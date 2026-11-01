using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.QueryApplication.Brands;
using EwaysVNext.Queries.Application.Paginations;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Products;

public sealed class CatalogAdminQueries(EwaysVNextDbContext db) : ICatalogAdminQueries
{
    private const int MaxPageSize = 200;

    public async Task<PaginatedQuery<BrandAdminRow>> ListBrandsAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

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

        var pageCount = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PaginatedQuery<BrandAdminRow>(page, pageSize, pageCount, totalCount, items);
    }
}
