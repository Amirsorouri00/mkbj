using EwaysVNext.Modules.Catalog.Application.Brands.Domain;

namespace EwaysVNext.Modules.Catalog.Application.Brands.Ports.Outbound;

public interface IBrandRepository
{
    Task<Brand?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Resolves any spelling of a brand — canonical name or alias — to the one brand that owns it.
    /// This is what an import calls for every row, so a miss must be cheap and a hit exact.
    /// </summary>
    Task<Brand?> FindByNameOrAliasAsync(string anyName, CancellationToken ct = default);

    Task AddAsync(Brand brand, CancellationToken ct = default);

    Task<bool> ExistsAsync(long id, CancellationToken ct = default);
}
