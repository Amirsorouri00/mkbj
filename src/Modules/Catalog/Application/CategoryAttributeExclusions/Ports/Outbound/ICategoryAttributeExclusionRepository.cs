using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Outbound;

public interface ICategoryAttributeExclusionRepository
{
    Task<CategoryAttributeExclusion?> GetByCategoryAndSourceAsync(long categoryId, long sourceAssignmentId, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryAttributeExclusion>> ListByCategoryAsync(long categoryId, CancellationToken ct = default);
    Task AddAsync(CategoryAttributeExclusion exclusion, CancellationToken ct = default);
    Task RemoveAsync(CategoryAttributeExclusion exclusion, CancellationToken ct = default);
}
