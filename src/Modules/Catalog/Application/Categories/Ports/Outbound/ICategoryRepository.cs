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

    /// <summary>
    /// Walks a root-to-leaf path of canonical keys (or aliases) to the category it names, or null
    /// if any step is missing.
    /// <para>
    /// A path rather than a single key because a key is only unique within its parent — "Kettles"
    /// under Kitchen and "Kettles" under Outdoors are two categories, and a feed that says only
    /// "Kettles" has not said which.
    /// </para>
    /// </summary>
    Task<CategoryEntity?> FindByCanonicalPathAsync(
        IReadOnlyList<string> canonicalKeyPath, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryHierarchyNode>> GetHierarchyAsync(
        long? ancestorId, long? subtreeRootId, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryEntity>> GetSubtreeForUpdateAsync(
        long rootId, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryEntity>> GetSubtreesForUpdateAsync(
        IReadOnlyCollection<long> rootIds, CancellationToken ct = default);
    Task AddAsync(CategoryEntity category, CancellationToken ct = default);
}

public sealed record CategoryHierarchyNode(long Id, long? ParentId);
