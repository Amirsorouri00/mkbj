using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using EwaysVNext.Outcomes;
using EwaysVNext.Queries.Application.Paginations;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;

public interface IAttributeBlueprintQueryService
{
    Task<Result<AttributeBlueprintSnapshot>> GetByIdAsync(long id, CancellationToken ct = default);

    Task<Result<AttributeBlueprintUsageSnapshot>> GetUsageAsync(long id, CancellationToken ct = default);

    Task<PaginatedQuery<AttributeBlueprintSnapshot>> ListAsync(int page, int pageSize, bool includeDeleted = false, AttributeBlueprintFilter? filter = null, CancellationToken ct = default);
}
