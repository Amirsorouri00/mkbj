using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;
using ProductEntity = EwaysVNext.Modules.Catalog.Application.Products.Domain.Product;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Products;

internal sealed class ProductRepository : EfCoreRepository<ProductEntity, long>, IProductRepository
{
    private readonly EwaysVNextDbContext _db;
    public ProductRepository(EwaysVNextDbContext db) : base(db) => _db = db;

    public new async Task<ProductEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<ProductEntity?> GetBySkuAsync(string sku, CancellationToken ct = default)
    {
        if (!Sku.TryCreate(sku, out var value)) return null;
        return await _db.Products.FirstOrDefaultAsync(p => p.Sku == value, ct);
    }

    public async Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default)
    {
        if (!Sku.TryCreate(sku, out var value)) return false;
        return await _db.Products.AnyAsync(p => p.Sku == value, ct);
    }

    public async Task<IReadOnlyList<ProductEntity>> ListAsync(int page, int pageSize, CancellationToken ct = default)
        => await _db.Products.OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

    public async Task<IReadOnlyList<ProductEntity>> FindWithReclaimableReservationsAsync(
        DateTimeOffset utcNow, TimeSpan committedRetention, CancellationToken ct = default)
    {
        var pruneBefore = utcNow - committedRetention;
        return await _db.Products
            .Where(p => p.Reservations.Any(r =>
                (!r.IsCommitted && r.ExpiresAt <= utcNow)
                || (r.IsCommitted && r.CommittedAt != null && r.CommittedAt <= pruneBefore)))
            .ToListAsync(ct);
    }

}
