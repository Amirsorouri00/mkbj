using Microsoft.EntityFrameworkCore;
using Mkbj.Database;
using Mkbj.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;

namespace Mkbj.Modules.Catalog.Adapter.Outbound.Dependencies.AttributeBlueprints;

internal sealed class UnavailableAttributeBlueprintDependencyGateway(MkbjDbContext db) : IAttributeBlueprintDependencyGateway
{
    public Task<AttributeBlueprintDependencies> GetDependenciesAsync(long attributeBlueprintId, CancellationToken ct = default)
        => throw new NotImplementedException("Product attribute values and category attribute mappings are not available yet.");

    public Task RemoveCategoryMappingsAsync(long attributeBlueprintId, CancellationToken ct = default)
        => throw new NotImplementedException("Category attribute mappings are not available yet.");

    public Task<int> CountProductValuesForOptionAsync(long attributeBlueprintId, long optionId, CancellationToken ct = default)
        => throw new NotImplementedException("Product attribute values are not available yet.");

    public Task RemoveProductValuesForOptionAsync(long attributeBlueprintId, long optionId, CancellationToken ct = default)
        => throw new NotImplementedException("Product attribute values are not available yet.");

    public async Task<int> CountActiveCategoryAssignmentsAsync(long attributeBlueprintId, CancellationToken ct = default)
        => await db.CategoryAttributeAssignments
            .CountAsync(a => a.AttributeBlueprintId == attributeBlueprintId && a.RemovedAt == null, ct);
}
