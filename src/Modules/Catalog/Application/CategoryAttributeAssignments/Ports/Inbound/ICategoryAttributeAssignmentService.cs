using EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Inbound;


public interface ICategoryAttributeAssignmentService
{
    Task<Result<CategoryAttributeAssignmentSnapshot>> AssignAsync(
        long categoryId,
        long attributeBlueprintId,
        bool isRequired,
        bool isFilterable,
        bool isSearchable,
        bool isInTitle,
        int? titleOrder,
        bool isPrimary,
        bool isVariantGenerating,
        CancellationToken ct = default);

    Task<Result<CategoryAttributeAssignmentSnapshot>> UnassignAsync(long assignmentId, CancellationToken ct = default);
    Task<Result<CategoryAttributeAssignmentSnapshot>> SetRequiredAsync(long assignmentId, bool isRequired, CancellationToken ct = default);
    Task<Result<CategoryAttributeAssignmentSnapshot>> SetFilterableAsync(long assignmentId, bool isFilterable, CancellationToken ct = default);
    Task<Result<CategoryAttributeAssignmentSnapshot>> SetSearchableAsync(long assignmentId, bool isSearchable, CancellationToken ct = default);
    Task<Result<CategoryAttributeAssignmentSnapshot>> SetTitleConfigurationAsync(long assignmentId, bool isInTitle, int? titleOrder, CancellationToken ct = default);
    Task<Result<CategoryAttributeAssignmentSnapshot>> ReorderAsync(long assignmentId, int titleOrder, CancellationToken ct = default);
    Task<Result<CategoryAttributeAssignmentSnapshot>> SetPrimaryAsync(long assignmentId, bool isPrimary, CancellationToken ct = default);
    Task<Result<CategoryAttributeAssignmentSnapshot>> SetVariantGeneratingAsync(long assignmentId, bool isVariantGenerating, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CategoryAttributeAssignmentSnapshot>>> ListByCategoryAsync(long categoryId, CancellationToken ct = default);
}
