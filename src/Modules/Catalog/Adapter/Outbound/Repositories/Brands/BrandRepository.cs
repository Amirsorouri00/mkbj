using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Brands.Domain;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Common;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Repositories.Brands;

internal sealed class BrandRepository(EwaysVNextDbContext db)
    : EfCoreRepository<Brand, long>(db), IBrandRepository
{
    public new async Task<Brand?> GetByIdAsync(long id, CancellationToken ct = default)
        => await db.Brands
            .Include(brand => brand.Aliases)
            .FirstOrDefaultAsync(brand => brand.Id == id, ct);

    public async Task<Brand?> FindByNameOrAliasAsync(string anyName, CancellationToken ct = default)
    {
        // The caller holds a supplier's wording, so it is canonicalised here rather than being
        // the caller's job to know how the column is stored.
        if (!CanonicalKey.TryFrom(anyName, out var canonical))
            return null;

        return await db.Brands
            .Include(brand => brand.Aliases)
            .FirstOrDefaultAsync(
                brand => brand.CanonicalName == canonical ||
                         brand.Aliases.Any(alias => alias.Alias == canonical), ct);
    }

    public async Task<bool> ExistsAsync(long id, CancellationToken ct = default)
        => await db.Brands.AnyAsync(brand => brand.Id == id, ct);
}
