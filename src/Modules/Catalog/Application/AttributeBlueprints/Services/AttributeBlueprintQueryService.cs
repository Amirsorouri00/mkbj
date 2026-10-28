using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using EwaysVNext.Outcomes;
using EwaysVNext.Queries.Application.Paginations;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Services;

public sealed class AttributeBlueprintQueryService(IAttributeBlueprintRepository repository, IAttributeBlueprintDependencyGateway dependencyGateway) : IAttributeBlueprintQueryService
{
    public async Task<Result<AttributeBlueprintSnapshot>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var snapshot = await repository.GetSnapshotByIdAsync(id, ct);
        return snapshot is null
            ? Result.Failure<AttributeBlueprintSnapshot>(new Error("ATTRIBUTE_BLUEPRINT_NOT_FOUND", "Attribute blueprint was not found."))
            : Result.Success(snapshot);
    }

    public Task<PaginatedQuery<AttributeBlueprintSnapshot>> ListAsync(int page, int pageSize, bool includeDeleted = false, AttributeBlueprintFilter? filter = null, CancellationToken ct = default)
        => repository.ListSnapshotsAsync(page, pageSize, includeDeleted, filter, ct);

    public async Task<Result<AttributeBlueprintUsageSnapshot>> GetUsageAsync(long id, CancellationToken ct = default)
    {
        var blueprint = await repository.GetSnapshotByIdAsync(id, ct);
        if (blueprint is null)
        {
            return Result.Failure<AttributeBlueprintUsageSnapshot>(new Error("ATTRIBUTE_BLUEPRINT_NOT_FOUND", "Attribute blueprint was not found."));
        }

        var dependencies = await dependencyGateway.GetDependenciesAsync(id, ct);
        return Result.Success(new AttributeBlueprintUsageSnapshot(id, dependencies.ProductValueCount, dependencies.CategoryMappingCount));
    }
}
