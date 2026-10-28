using EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Abstractions;

public interface ICategoryAttributeAssignmentQueryService
{
    Task<IReadOnlyList<CategoryAttributeAssignmentAdminRow>> GetAdminRowsByCategoryAsync(long categoryId, CancellationToken ct = default);
}
