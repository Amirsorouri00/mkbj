using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using Microsoft.EntityFrameworkCore;
using CategoryEntity = EwaysVNext.Modules.Catalog.Application.Categories.Domain.Category;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Categories;

internal sealed class CategoryRepository(EwaysVNextDbContext db) : ICategoryRepository
{

    public Task<CategoryEntity?> GetByIdAsync(long id, CancellationToken ct = default) =>
        db.Categories.FirstOrDefaultAsync(category => category.Id == id, ct);

    public async Task<IReadOnlyList<CategoryEntity>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return Array.Empty<CategoryEntity>();

        return await db.Categories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(category => ids.Contains(category.Id))
            .ToListAsync(ct);
    }

    public Task<bool> NameExistsAtLevelAsync(string normalizedName, long? parentId,
        long? excludingCategoryId = null, CancellationToken ct = default) =>
        db.Categories.AsNoTracking().AnyAsync(category =>
            category.Status != CategoryStatus.Deleted
            && category.ParentId == parentId
            && category.NormalizedName == normalizedName
            && (!excludingCategoryId.HasValue || category.Id != excludingCategoryId.Value), ct);

    public async Task<bool> SlugExistsAsync(string slug, long? excludingCategoryId = null,
        CancellationToken ct = default)
    {
        CategorySlug value;
        try { value = CategorySlug.Create(slug); }
        catch { return false; }

        return await db.Categories.AsNoTracking().AnyAsync(category =>
            category.Status != CategoryStatus.Deleted && category.Slug == value
            && (!excludingCategoryId.HasValue || category.Id != excludingCategoryId.Value), ct);
    }

    public Task<bool> HasChildrenAsync(long categoryId, CancellationToken ct = default) =>
        db.Categories.AsNoTracking().AnyAsync(category =>
            category.ParentId == categoryId && category.Status != CategoryStatus.Deleted, ct);

    public async Task<IReadOnlyList<CategoryHierarchyNode>> GetHierarchyAsync(
    long? ancestorId,
    long? subtreeRootId,
    CancellationToken ct = default)
    {
        var nodes = await db.Categories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(category => new CategoryHierarchyNode(
                category.Id,
                category.ParentId))
            .ToListAsync(ct);

        var nodesById = nodes.ToDictionary(node => node.Id);

        var childrenByParentId = nodes
            .Where(node => node.ParentId.HasValue)
            .ToLookup(node => node.ParentId!.Value);

        var includedIds = new HashSet<long>();

        AddAncestors();
        AddSubtree();

        return nodes
            .Where(node => includedIds.Contains(node.Id))
            .ToArray();

        void AddAncestors()
        {
            var visited = new HashSet<long>();
            var currentId = ancestorId;

            while (currentId is long id &&
                   visited.Add(id) &&
                   nodesById.TryGetValue(id, out var node))
            {
                includedIds.Add(node.Id);
                currentId = node.ParentId;
            }
        }

        void AddSubtree()
        {
            if (subtreeRootId is not long rootId)
                return;

            var visited = new HashSet<long>();
            var pending = new Stack<long>();
            pending.Push(rootId);

            while (pending.TryPop(out var id))
            {
                if (!visited.Add(id))
                    continue;

                if (!nodesById.TryGetValue(id, out var node))
                    continue;

                includedIds.Add(node.Id);

                foreach (var child in childrenByParentId[id])
                    pending.Push(child.Id);
            }
        }
    }

    public async Task<IReadOnlyList<CategoryEntity>> GetSubtreeForUpdateAsync(
        long rootId, CancellationToken ct = default) =>
        await GetSubtreesForUpdateAsync([rootId], ct);

    public async Task<IReadOnlyList<CategoryEntity>> GetSubtreesForUpdateAsync(
    IReadOnlyCollection<long> rootIds,
    CancellationToken ct = default)
    {
        if (rootIds.Count == 0)
            return Array.Empty<CategoryEntity>();

        var topology = await db.Categories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(category => new
            {
                category.Id,
                category.ParentId
            })
            .ToListAsync(ct);

        var childrenByParentId = topology.ToLookup(
            category => category.ParentId);

        var visited = new HashSet<long>();
        var pending = new Queue<long>(rootIds.Distinct());

        while (pending.TryDequeue(out var categoryId))
        {
            ct.ThrowIfCancellationRequested();

            if (!visited.Add(categoryId))
                continue;

            foreach (var child in childrenByParentId[categoryId])
                pending.Enqueue(child.Id);
        }

        if (visited.Count == 0)
            return Array.Empty<CategoryEntity>();

        var subtreeIds = visited.ToArray();

        // No AsNoTracking: these entities must be tracked so that
        // DeactivateManyAsync can modify and persist them.
        return await db.Categories
            .IgnoreQueryFilters()
            .Where(category => subtreeIds.Contains(category.Id))
            .ToListAsync(ct);
    }

    public async Task AddAsync(CategoryEntity category, CancellationToken ct = default) =>
        await db.Categories.AddAsync(category, ct);

    private sealed class HierarchyRow
    {
        public long Id { get; init; }
        public long? ParentId { get; init; }
    }
}
