
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using EwaysVNext.Queries.Application.Paginations;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;

public interface IAttributeBlueprintRepository
{
    Task<AttributeBlueprint?> GetByIdAsync(long id, CancellationToken ct = default);

    Task AddAsync(AttributeBlueprint attributeBlueprint, CancellationToken ct = default);

    Task<bool> NameExistsAsync(string name, long? excludingId = null, CancellationToken ct = default);

    Task<AttributeBlueprintSnapshot?> GetSnapshotByIdAsync(long id, CancellationToken ct = default);

    Task<PaginatedQuery<AttributeBlueprintSnapshot>> ListSnapshotsAsync(int page, int pageSize, bool includeDeleted = false, AttributeBlueprintFilter? filter = null, CancellationToken ct = default);

    Task<IReadOnlyList<AttributeBlueprintSnapshot>> GetSnapshotsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

}
