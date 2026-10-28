using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Inbound;

public interface ICategoryAttributeExclusionService
{
    Task<Result<CategoryAttributeExclusionSnapshot>> ExcludeAsync(long categoryId, long sourceAssignmentId, CancellationToken ct = default);
    Task<Result<CategoryAttributeExclusionSnapshot>> IncludeAsync(long categoryId, long sourceAssignmentId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CategoryAttributeExclusionSnapshot>>> SetExclusionsAsync(
        long categoryId, IReadOnlyList<long> excludedSourceAssignmentIds, CancellationToken ct = default);
}
