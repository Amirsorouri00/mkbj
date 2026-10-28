using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Categories;

/// <summary>Back-office read port. Unlike public queries, it never applies effective-visibility filtering.</summary>
public interface ICategoryAdminQueries
{
    IQueryable<CategoryAdminDto> Search(CategoryAdminFilterDto? filter);
    Task<Result<CategoryAdminDto>> GetByIdAsync(long categoryId, CancellationToken ct = default);
    Task<Result<CategoryAdminDto?>> GetParentAsync(
        long categoryId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CategoryAdminTreeNodeDto>>> GetDescendantsAsync(
        long categoryId, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryAdminDto>> GetChildrenAsync(long? parentId, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryAdminTreeNodeDto>> GetHierarchyAsync(
        CancellationToken ct = default);
    Task<Result<IQueryable<long>>> GetProductIdsAsync(
        long categoryId, CancellationToken ct = default);
}
