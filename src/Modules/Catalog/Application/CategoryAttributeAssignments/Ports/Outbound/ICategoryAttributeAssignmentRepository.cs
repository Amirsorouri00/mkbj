using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;
using EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;

public interface ICategoryAttributeAssignmentRepository
{
    Task<CategoryAttributeAssignment?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<CategoryAttributeAssignment?> GetActiveByCategoryAndAttributeAsync(long categoryId, long attributeBlueprintId, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryAttributeAssignment>> ListByCategoryAsync(long categoryId, CancellationToken ct = default);
    Task AddAsync(CategoryAttributeAssignment aggregate, CancellationToken ct = default);

    Task<int> CountActivePrimaryInCategoriesAsync(IReadOnlyList<long> categoryIds, long? excludingAssignmentId = null, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryAttributeAssignmentAdminRow>> GetAdminRowsByCategoryAsync(long categoryId, CancellationToken ct = default);
}
