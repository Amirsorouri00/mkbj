using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using CategoryEntity = EwaysVNext.Modules.Catalog.Application.Categories.Domain.Category;

namespace EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;

public interface ICategoryRepository
{
    Task<CategoryEntity?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryEntity>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);
    Task<bool> NameExistsAtLevelAsync(string normalizedName, long? parentId,
        long? excludingCategoryId = null, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, long? excludingCategoryId = null, CancellationToken ct = default);
    Task<bool> HasChildrenAsync(long categoryId, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryHierarchyNode>> GetHierarchyAsync(
        long? ancestorId, long? subtreeRootId, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryEntity>> GetSubtreeForUpdateAsync(
        long rootId, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryEntity>> GetSubtreesForUpdateAsync(
        IReadOnlyCollection<long> rootIds, CancellationToken ct = default);
    Task AddAsync(CategoryEntity category, CancellationToken ct = default);
}

public sealed record CategoryHierarchyNode(long Id, long? ParentId);
