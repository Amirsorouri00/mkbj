using Mkbj.Modules.Catalog.QueryApplication.AttributeBlueprints;
using Mkbj.Outcomes;
using Mkbj.Queries.Application.Paginations;

namespace Mkbj.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;

public interface IAttributeBlueprintQueryService
{
    Task<Result<AttributeBlueprintSnapshot>> GetByIdAsync(long id, CancellationToken ct = default);

    Task<Result<AttributeBlueprintUsageSnapshot>> GetUsageAsync(long id, CancellationToken ct = default);

    Task<PaginatedQuery<AttributeBlueprintSnapshot>> ListAsync(int page, int pageSize, bool includeDeleted = false, AttributeBlueprintFilter? filter = null, CancellationToken ct = default);
}
