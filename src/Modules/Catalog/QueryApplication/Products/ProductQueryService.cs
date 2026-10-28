using EwaysVNext.Outcomes;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Contracts.Products;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Products;

public sealed class ProductQueryService(EwaysVNextDbContext db) : IProductQueryService
{
    public async Task<Result<ProductSnapshot>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var snapshot = await db.Products.AsNoTracking()
            .Where(product => product.Id == id)
            .Select(product => new ProductSnapshot(product.Id, product.Name,
                product.Price.Amount, product.Price.Currency))
            .FirstOrDefaultAsync(ct);
        return snapshot is null
            ? Result.Failure<ProductSnapshot>(new Error("PRODUCT_NOT_FOUND", "Product was not found."))
            : Result.Success(snapshot);
    }

    public async Task<IReadOnlyList<ProductSnapshot>> ListAsync(
        int page, int pageSize, CancellationToken ct = default) =>
        await db.Products.AsNoTracking()
            .OrderByDescending(product => product.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductSnapshot(product.Id, product.Name,
                product.Price.Amount, product.Price.Currency))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProductAdminRow>> GetProductRowsAsync(
        string? search, bool includeInactive, int page, int pageSize,
        CancellationToken ct = default)
    {
        var query = db.Products.AsNoTracking();
        if (!includeInactive) query = query.Where(product => product.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = Sku.TryCreate(term, out var exactSku)
                ? query.Where(product => product.Name.Contains(term) || product.Sku == exactSku)
                : query.Where(product => product.Name.Contains(term));
        }

        return await query
            .OrderByDescending(product => product.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductAdminRow(product.Id, product.Sku.Value,
                product.Name, product.Price.Amount, product.Price.Currency, product.IsActive))
            .ToListAsync(ct);
    }
}
