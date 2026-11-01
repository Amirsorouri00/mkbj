using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;
using ProductEntity = EwaysVNext.Modules.Catalog.Application.Products.Domain.Product;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Products;

internal sealed class ProductRepository(EwaysVNextDbContext db)
    : EfCoreRepository<ProductEntity, long>(db), IProductRepository
{
    /// <summary>
    /// Every write path needs the children, so they are always included rather than left to each
    /// caller to remember — a product loaded without its variants silently cannot enforce its
    /// signature and SKU rules.
    /// </summary>
    private IQueryable<ProductEntity> Full => db.Products
        .Include(product => product.Variants)
        .Include(product => product.Media)
        .Include(product => product.Identifiers);

    public new async Task<ProductEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        => await Full.FirstOrDefaultAsync(product => product.Id == id, ct);

    public async Task<IReadOnlyList<ProductEntity>> GetByIdsAsync(IReadOnlyCollection<long> ids,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return [];

        return await Full.Where(product => ids.Contains(product.Id)).ToListAsync(ct);
    }

    public async Task<ProductEntity?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;

        var normalized = slug.Trim();
        return await Full.FirstOrDefaultAsync(product => product.Slug == normalized, ct);
    }

    public async Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default)
    {
        if (!Sku.TryCreate(sku, out var value)) return false;

        return await db.Products.AnyAsync(
            product => product.Variants.Any(variant => variant.Sku == value), ct);
    }

    public async Task<ProductEntity?> FindByIdentifierAsync(ProductIdentifierType type,
        string normalizedValue, string? sourceDomain, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(normalizedValue)) return null;

        return await Full.FirstOrDefaultAsync(product => product.Identifiers.Any(identifier =>
            identifier.Type == type &&
            identifier.Value == normalizedValue &&
            identifier.SourceDomain == sourceDomain), ct);
    }

    public async Task<ProductEntity?> FindByBrandAndMpnAsync(long brandId, string mpn,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mpn)) return null;

        // Matched against the denormalized column rather than the identifier table, because that
        // is the pair the unique index covers.
        var normalized = ProductIdentifier.Normalize(ProductIdentifierType.Mpn, mpn, null);
        return await Full.FirstOrDefaultAsync(
            product => product.BrandId == brandId && product.Mpn == normalized, ct);
    }

    public async Task<ProductEntity?> FindByVariantIdAsync(long variantId, CancellationToken ct = default)
        => await Full.FirstOrDefaultAsync(
            product => product.Variants.Any(variant => variant.Id == variantId), ct);
}
