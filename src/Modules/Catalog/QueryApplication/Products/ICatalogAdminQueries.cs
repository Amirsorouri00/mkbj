using EwaysVNext.Modules.Catalog.QueryApplication.Brands;
using EwaysVNext.Queries.Application.Paginations;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Products;

/// <summary>
/// The catalogue's administrative read side. Product methods arrive with task 14; brands are
/// here first because the brand admin screens need them now.
/// </summary>
public interface ICatalogAdminQueries
{
    Task<PaginatedQuery<BrandAdminRow>> ListBrandsAsync(
        string? search, int page, int pageSize, CancellationToken ct = default);
}
