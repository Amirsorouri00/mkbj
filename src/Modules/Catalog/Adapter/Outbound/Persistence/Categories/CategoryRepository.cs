using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CategoryEntity = EwaysVNext.Modules.Catalog.Application.Categories.Domain.Category;
using System.Text.Json;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Categories;

internal sealed class CategoryRepository(EwaysVNextDbContext db) : ICategoryRepository
{
    private const string SqlServerHierarchyQuery = """
        WITH Hierarchy AS
        (
            SELECT category.Id, category.ParentId,
                CAST(CONCAT('|', category.Id, '|') AS varchar(max)) AS VisitedPath,
                seed.Direction
            FROM [catalog].[Category] AS category
            INNER JOIN (VALUES (@ancestorId, 0), (@subtreeRootId, 1)) AS seed(Id, Direction)
                ON category.Id = seed.Id

            UNION ALL

            SELECT nextCategory.Id, nextCategory.ParentId,
                CAST(hierarchy.VisitedPath + CONVERT(varchar(20), nextCategory.Id) + '|'
                    AS varchar(max)) AS VisitedPath,
                hierarchy.Direction
            FROM [catalog].[Category] AS nextCategory
            INNER JOIN Hierarchy AS hierarchy ON
                (hierarchy.Direction = 0 AND nextCategory.Id = hierarchy.ParentId)
                OR (hierarchy.Direction = 1 AND nextCategory.ParentId = hierarchy.Id)
            WHERE CHARINDEX(CONCAT('|', nextCategory.Id, '|'), hierarchy.VisitedPath) = 0
        )
        SELECT DISTINCT Id, ParentId
        FROM Hierarchy
        OPTION (MAXRECURSION 32767)
        """;

    private const string SqlServerTrackedSubtreeQuery = """
        WITH Hierarchy AS
        (
            SELECT category.Id, category.ParentId, category.Name, category.NormalizedName, category.Slug,
                category.Description, category.DisplayOrder, category.Status,
                category.DeletedAt, category.CreatedAt,
                category.UpdatedAt, category.RowVersion,
                CAST(CONCAT('|', category.Id, '|') AS varchar(max)) AS VisitedPath
            FROM [catalog].[Category] AS category
            INNER JOIN OPENJSON(@rootIds) WITH (Id bigint '$') AS root
                ON root.Id = category.Id

            UNION ALL

            SELECT child.Id, child.ParentId, child.Name, child.NormalizedName, child.Slug,
                child.Description, child.DisplayOrder, child.Status,
                child.DeletedAt, child.CreatedAt,
                child.UpdatedAt, child.RowVersion,
                CAST(hierarchy.VisitedPath + CONVERT(varchar(20), child.Id) + '|'
                    AS varchar(max)) AS VisitedPath
            FROM [catalog].[Category] AS child
            INNER JOIN Hierarchy AS hierarchy ON child.ParentId = hierarchy.Id
            WHERE CHARINDEX(CONCAT('|', child.Id, '|'), hierarchy.VisitedPath) = 0
        )
        SELECT DISTINCT Id, ParentId, Name, NormalizedName, Slug, Description, DisplayOrder, Status,
            DeletedAt, CreatedAt, UpdatedAt, RowVersion
        FROM Hierarchy
        OPTION (MAXRECURSION 32767)
        """;

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

    //used raw query :

    //public async Task<IReadOnlyList<CategoryHierarchyNode>> GetHierarchyAsync(
    //    long? ancestorId, long? subtreeRootId, CancellationToken ct = default)
    //{
    //    if (db.Database.IsSqlServer())
    //    {
    //        var rows = await db.Database.SqlQueryRaw<HierarchyRow>(
    //                SqlServerHierarchyQuery,
    //                new SqlParameter("@ancestorId", (object?)ancestorId ?? DBNull.Value),
    //                new SqlParameter("@subtreeRootId", (object?)subtreeRootId ?? DBNull.Value))
    //            .ToListAsync(ct);

    //        return rows.Select(row =>
    //            new CategoryHierarchyNode(row.Id, row.ParentId)).ToArray();
    //    }

    //    return await db.Categories
    //        .IgnoreQueryFilters()
    //        .AsNoTracking()
    //        .Select(category => new CategoryHierarchyNode(category.Id, category.ParentId))
    //        .ToListAsync(ct);
    //}

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


    /// used raw query

    //public async Task<IReadOnlyList<CategoryEntity>> GetSubtreesForUpdateAsync(
    //    IReadOnlyCollection<long> rootIds, CancellationToken ct = default)
    //{
    //    if (db.Database.IsSqlServer())
    //    {
    //        return await db.Categories
    //            .FromSqlRaw(SqlServerTrackedSubtreeQuery,
    //                new SqlParameter("@rootIds", JsonSerializer.Serialize(rootIds)))
    //            .ToListAsync(ct);
    //    }

    //    var topology = await db.Categories
    //        .IgnoreQueryFilters()
    //        .ToListAsync(ct);
    //    var byId = topology.ToDictionary(category => category.Id);
    //    var children = topology.ToLookup(category => category.ParentId);
    //    var visited = new HashSet<long>();
    //    var pending = new Queue<long>();
    //    var subtree = new List<CategoryEntity>();
    //    foreach (var rootId in rootIds.Distinct()) pending.Enqueue(rootId);

    //    while (pending.Count > 0)
    //    {
    //        ct.ThrowIfCancellationRequested();
    //        var id = pending.Dequeue();
    //        if (!visited.Add(id) || !byId.TryGetValue(id, out var category)) continue;
    //        subtree.Add(category);
    //        foreach (var child in children[id]) pending.Enqueue(child.Id);
    //    }

    //    return subtree;
    //}

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
