using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using EwaysVNext.Modules.Catalog.QueryApplication.Abstractions;
using EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Services;

public sealed class CategoryAttributeAssignmentQueryService(ICategoryAttributeAssignmentRepository assignments)
    : ICategoryAttributeAssignmentQueryService
{
    public Task<IReadOnlyList<CategoryAttributeAssignmentAdminRow>> GetAdminRowsByCategoryAsync(long categoryId, CancellationToken ct = default)
        => assignments.GetAdminRowsByCategoryAsync(categoryId, ct);
}
