using Mkbj.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using Mkbj.Modules.Catalog.QueryApplication.Abstractions;
using Mkbj.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

namespace Mkbj.Modules.Catalog.Application.CategoryAttributeAssignments.Services;

public sealed class CategoryAttributeAssignmentQueryService(ICategoryAttributeAssignmentRepository assignments)
    : ICategoryAttributeAssignmentQueryService
{
    public Task<IReadOnlyList<CategoryAttributeAssignmentAdminRow>> GetAdminRowsByCategoryAsync(long categoryId, CancellationToken ct = default)
        => assignments.GetAdminRowsByCategoryAsync(categoryId, ct);
}
