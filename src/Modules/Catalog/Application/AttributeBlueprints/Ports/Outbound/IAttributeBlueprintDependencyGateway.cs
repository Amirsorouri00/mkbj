namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;

public interface IAttributeBlueprintDependencyGateway
{
    Task<AttributeBlueprintDependencies> GetDependenciesAsync(long attributeBlueprintId, CancellationToken ct = default);
    Task RemoveCategoryMappingsAsync(long attributeBlueprintId, CancellationToken ct = default);
    Task<int> CountProductValuesForOptionAsync(long attributeBlueprintId, long optionId, CancellationToken ct = default);
    Task RemoveProductValuesForOptionAsync(long attributeBlueprintId, long optionId, CancellationToken ct = default);
    Task<int> CountActiveCategoryAssignmentsAsync(long attributeBlueprintId, CancellationToken ct = default);
}

public sealed record AttributeBlueprintDependencies(int ProductValueCount, int CategoryMappingCount);
