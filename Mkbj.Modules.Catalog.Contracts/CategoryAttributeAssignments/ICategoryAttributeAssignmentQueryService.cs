using Mkbj.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

namespace Mkbj.Modules.Catalog.QueryApplication.Abstractions;

public interface ICategoryAttributeAssignmentQueryService
{
    Task<IReadOnlyList<CategoryAttributeAssignmentAdminRow>> GetAdminRowsByCategoryAsync(long categoryId, CancellationToken ct = default);
}
