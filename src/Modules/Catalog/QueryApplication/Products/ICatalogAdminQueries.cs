using EwaysVNext.Modules.Catalog.QueryApplication.Brands;
using EwaysVNext.Outcomes;
using EwaysVNext.Queries.Application.Paginations;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Products;

/// <summary>
/// The catalogue's administrative read side. Unlike the storefront's, these projections include
/// drafts, deactivated products and inactive brands — the admin screens are where someone goes to
/// find exactly those.
/// </summary>
public interface ICatalogAdminQueries
{
    Task<PaginatedQuery<BrandAdminRow>> ListBrandsAsync(
        string? search, int page, int pageSize, CancellationToken ct = default);

    Task<PaginatedQuery<ProductAdminRowDto>> ListProductsAsync(
        ProductAdminFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task<Result<ProductAdminDetailDto>> GetProductAsync(long productId, CancellationToken ct = default);
}
