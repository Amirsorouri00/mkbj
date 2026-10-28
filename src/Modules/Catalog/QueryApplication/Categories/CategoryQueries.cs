using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;
using static EwaysVNext.Modules.Catalog.QueryApplication.Categories.CategoryQueryShapes;
using EwaysVNext.Database;


namespace EwaysVNext.Modules.Catalog.QueryApplication.Categories;

/// <summary>
/// Read-only projections for public category promotion queries. Every query uses
/// no-tracking projections; no aggregate or association entity is materialised.
/// </summary>
public sealed class CategoryQueries(EwaysVNextDbContext db) : ICategoryQueries
{
    private const int MaxPageSize = 100;

    public async Task<IReadOnlyList<CategoryTreeNodeDto>> GetTreeAsync(
        CancellationToken ct = default)
    {
        // The public tree deliberately uses one projection. Ancestor visibility is resolved
        // in memory from this small hierarchy result, avoiding one query per tree node.
        var rows = await CategoryQueryShapes.Categories(db).ToListAsync(ct);

        var allRows = rows.ToDictionary(row => row.Id);
        var visible = rows
            .Where(row => IsEffectivelyVisibleInMemory(row, allRows, new HashSet<long>()))
            .ToDictionary(row => row.Id);

        var roots = visible.Values
            .Where(row => !row.ParentId.HasValue)
            .OrderBy(row => row.DisplayOrder)
            .ThenBy(row => row.Id)
            .Select(row => BuildTreeNode(row, visible, new HashSet<long>()))
            .ToList();

        return roots;
    }

    public async Task<Result<CategoryDto>> GetByIdAsync(
        long categoryId, CancellationToken ct = default)
    {
        var row = await CategoryQueryShapes.CategoryById(db, categoryId)
            .SingleOrDefaultAsync(ct);

        if (row is null || !await IsEffectivelyVisibleAsync(row, ct))
            return Result.Failure<CategoryDto>(NotFound());

        return Result.Success(ToDto(row));
    }

    public async Task<Result<CategoryProductPageDto>> GetProductIdsAsync(
        long categoryId, int page, int pageSize, CancellationToken ct = default)
    {
        var validation = ValidatePaging(page, pageSize);
        if (validation is not null)
            return Result.Failure<CategoryProductPageDto>(validation);

        var category = await CategoryQueryShapes.CategoryById(db, categoryId)
            .SingleOrDefaultAsync(ct);

        if (category is null || !await IsEffectivelyVisibleAsync(category, ct))
            return Result.Failure<CategoryProductPageDto>(NotFound());

        var assignments = CategoryQueryShapes.ProductIdsForCategory(db, categoryId);
        var totalCount = await assignments.CountAsync(ct);
        var offset = (long)(page - 1) * pageSize;
        // Queryable.Skip accepts an Int32. Clamping an excessively distant page preserves
        // the public contract (an empty page) without allowing arithmetic overflow.
        var skip = offset > int.MaxValue ? int.MaxValue : (int)offset;
        var productIds = await CategoryQueryShapes
            .PagedProductIdsForCategory(db, categoryId, skip, pageSize)
            .ToListAsync(ct);

        return Result.Success(new CategoryProductPageDto(productIds, page, pageSize, totalCount));
    }

    public async Task<IReadOnlyList<CategoryDto>> GetByProductIdAsync(
        long productId, CancellationToken ct = default)
    {
        //if (productId == Guid.Empty) return [];

        var rows = await CategoryQueryShapes.ProductCategoriesForProduct(db, productId)
            .ToListAsync(ct);

        if (rows.Count == 0) return [];

        // Resolve all category ancestors from one bounded hierarchy projection. Doing this
        // per assigned category turns a product with many promotions into an N+1 query path.
        var hierarchy = await CategoryQueryShapes.Categories(db).ToListAsync(ct);
        var allRows = hierarchy.ToDictionary(row => row.Id);

        var result = new List<CategoryDto>(rows.Count);
        foreach (var row in rows)
        {
            if (IsEffectivelyVisibleInMemory(row, allRows, new HashSet<long>()))
                result.Add(ToDto(row));
        }

        return result;
    }

    private async Task<bool> IsEffectivelyVisibleAsync(CategoryProjection category, CancellationToken ct)
    {
        if (category.Status != CategoryStatus.Active) return false;

        var visited = new HashSet<long> { category.Id };
        var parentId = category.ParentId;
        while (parentId.HasValue)
        {
            if (!visited.Add(parentId.Value)) return false;

            var parent = await db.Categories
                .AsNoTracking()
                .Where(candidate => candidate.Id == parentId.Value)
                .Select(candidate => new AncestorProjection(
                    candidate.Id, candidate.ParentId, candidate.Status))
                .SingleOrDefaultAsync(ct);

            if (parent is null || parent.Status != CategoryStatus.Active) return false;
            parentId = parent.ParentId;
        }

        return true;
    }

    private static CategoryTreeNodeDto BuildTreeNode(
        CategoryProjection row,
        IReadOnlyDictionary<long, CategoryProjection> visible,
        HashSet<long> path)
    {
        if (!path.Add(row.Id))
            return new CategoryTreeNodeDto(
                row.Id, row.ParentId, row.Name, row.Slug, row.Description, row.DisplayOrder, []);

        var children = visible.Values
            .Where(candidate => candidate.ParentId == row.Id)
            .OrderBy(candidate => candidate.DisplayOrder)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => BuildTreeNode(candidate, visible, new HashSet<long>(path)))
            .ToList();

        return new CategoryTreeNodeDto(
            row.Id, row.ParentId, row.Name, row.Slug, row.Description, row.DisplayOrder, children);
    }

    private static bool IsEffectivelyVisibleInMemory(
        CategoryProjection category,
        IReadOnlyDictionary<long, CategoryProjection> allRows,
        HashSet<long> path)
    {
        if (category.Status != CategoryStatus.Active || !path.Add(category.Id)) return false;
        if (!category.ParentId.HasValue) return true;
        return allRows.TryGetValue(category.ParentId.Value, out var parent)
            && IsEffectivelyVisibleInMemory(parent, allRows, path);
    }

    private static CategoryDto ToDto(CategoryProjection row) =>
        new(row.Id, row.ParentId, row.Name, row.Slug, row.Description, row.DisplayOrder);

    private static Error? ValidatePaging(int page, int pageSize)
    {
        if (page < 1)
            return new Error("CATEGORY_PAGE_INVALID", "Page must be at least 1.");
        if (pageSize is < 1 or > MaxPageSize)
            return new Error("CATEGORY_PAGE_SIZE_INVALID", $"PageSize must be between 1 and {MaxPageSize}.");
        return null;
    }

    private static Error NotFound() =>
        new("CATEGORY_NOT_FOUND", "The category was not found.");

    private sealed record AncestorProjection(
        long Id, long? ParentId, CategoryStatus Status);
}
