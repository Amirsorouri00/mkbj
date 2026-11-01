using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using ProductEntity = EwaysVNext.Modules.Catalog.Application.Products.Domain.Product;

namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;

/// <summary>
/// Loading a product means loading its variants, media and identifiers: every write path touches
/// at least one of them, and a partially loaded aggregate cannot enforce its own rules.
/// </summary>
public interface IProductRepository
{
    Task<ProductEntity?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<ProductEntity>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    Task<ProductEntity?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default);

    /// <summary>
    /// The import's first question about a feed row. <paramref name="normalizedValue"/> must have
    /// come through <see cref="ProductIdentifier.Normalize"/>, or it will match nothing.
    /// </summary>
    Task<ProductEntity?> FindByIdentifierAsync(ProductIdentifierType type, string normalizedValue,
        string? sourceDomain, CancellationToken ct = default);

    Task<ProductEntity?> FindByBrandAndMpnAsync(long brandId, string mpn, CancellationToken ct = default);

    Task<ProductEntity?> FindByVariantIdAsync(long variantId, CancellationToken ct = default);

    Task AddAsync(ProductEntity aggregate, CancellationToken ct = default);
}
