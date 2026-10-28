using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;
using EwaysVNext.Database;

namespace EwaysVNext.Modules.Catalog.QueryApplication.Categories;

/// <summary>Administrative projections deliberately include inactive and soft-deleted categories.</summary>
public sealed class CategoryAdminQueries(EwaysVNextDbContext db) : ICategoryAdminQueries
{
    public IQueryable<CategoryAdminDto> Search(CategoryAdminFilterDto? filter)
    {
        var categories = ApplyFilter(
            db.Categories.IgnoreQueryFilters().AsNoTracking(),
            filter,
            db.Database.IsInMemory());
        return Projection(categories
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Id));
    }

    public async Task<Result<CategoryAdminDto>> GetByIdAsync(long categoryId, CancellationToken ct = default)
    {
        var query = db.Categories.IgnoreQueryFilters().AsNoTracking()
            .Where(category => category.Id == categoryId);
        var category = await Projection(query).SingleOrDefaultAsync(ct);
        return category is null
            ? Result.Failure<CategoryAdminDto>(NotFound())
            : Result.Success(category);
    }

    public async Task<Result<CategoryAdminDto?>> GetParentAsync(
        long categoryId, CancellationToken ct = default)
    {
        var category = await GetByIdAsync(categoryId, ct);
        if (category.IsFailure)
            return Result.Failure<CategoryAdminDto?>(category.Error!);
        if (!category.Value!.ParentId.HasValue)
            return Result.Success<CategoryAdminDto?>(null);

        var parent = await GetByIdAsync(category.Value.ParentId.Value, ct);
        return parent.IsFailure
            ? Result.Failure<CategoryAdminDto?>(parent.Error!)
            : Result.Success<CategoryAdminDto?>(parent.Value);
    }

    public async Task<Result<IReadOnlyList<CategoryAdminTreeNodeDto>>> GetDescendantsAsync(
        long categoryId, CancellationToken ct = default)
    {
        var hierarchy = await GetHierarchyAsync(ct);
        var source = FindNode(hierarchy, categoryId, ct);
        return source is null
            ? Result.Failure<IReadOnlyList<CategoryAdminTreeNodeDto>>(NotFound())
            : Result.Success<IReadOnlyList<CategoryAdminTreeNodeDto>>(source.Children);
    }

    public async Task<IReadOnlyList<CategoryAdminDto>> GetChildrenAsync(long? parentId,
        CancellationToken ct = default)
    {
        var query = db.Categories.IgnoreQueryFilters().AsNoTracking()
            .Where(category => category.ParentId == parentId)
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Id);
        return await Projection(query).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CategoryAdminTreeNodeDto>> GetHierarchyAsync(
        CancellationToken ct = default)
    {
        var rows = await Projection().ToListAsync(ct);
        var byId = rows.ToDictionary(row => row.Id);
        var children = rows.ToLookup(row => row.ParentId);
        var visited = new HashSet<long>();
        var result = new List<CategoryAdminTreeNodeDto>();
        var rootCandidates = rows
            .Where(row => row.ParentId is null || !byId.ContainsKey(row.ParentId.Value))
            .OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id);

        foreach (var root in rootCandidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Contains(root.Id)) result.Add(BuildNode(root, children, visited, [], ct));
        }

        foreach (var remaining in rows.OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id))
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Contains(remaining.Id)) result.Add(BuildNode(remaining, children, visited, [], ct));
        }

        return result;
    }

    public async Task<Result<IQueryable<long>>> GetProductIdsAsync(
        long categoryId, CancellationToken ct = default)
    {
        if (!await db.Categories.AsNoTracking().AnyAsync(category => category.Id == categoryId, ct))
            return Result.Failure<IQueryable<long>>(NotFound());

        var assignments = db.CategoryProducts.AsNoTracking()
            .Where(assignment => assignment.CategoryId == categoryId)
            .Select(assignment => assignment.ProductId)
            .OrderBy(productId => productId);
        return Result.Success<IQueryable<long>>(assignments);
    }

    private IQueryable<CategoryAdminDto> Projection() =>
        Projection(db.Categories.IgnoreQueryFilters().AsNoTracking());

    private IQueryable<CategoryAdminDto> Projection(IQueryable<Category> categories)
    {
        var allCategories = db.Categories.IgnoreQueryFilters().AsNoTracking();
        return categories.Select(category => new CategoryAdminDto
            {
                Id = category.Id,
                ParentId = category.ParentId,
                Name = category.Name,
                Slug = category.Slug.Value,
                Description = category.Description,
                DisplayOrder = category.DisplayOrder,
                Status = category.Status,
                Parent = category.ParentId == null
                    ? null
                    : allCategories
                        .Where(parent => parent.Id == category.ParentId)
                        .Select(parent => new CategoryAdminParentDto
                        {
                            Id = parent.Id,
                            Name = parent.Name
                        })
                        .SingleOrDefault(),
                ChildCount = allCategories.Count(child =>
                    child.ParentId == category.Id && child.Status != CategoryStatus.Deleted)
            });
    }

    private static CategoryAdminTreeNodeDto BuildNode(
        CategoryAdminDto row,
        ILookup<long?, CategoryAdminDto> children,
        HashSet<long> visited,
        HashSet<long> path,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        visited.Add(row.Id);
        path.Add(row.Id);
        var descendants = new List<CategoryAdminTreeNodeDto>();
        foreach (var child in children[row.Id].OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id))
        {
            ct.ThrowIfCancellationRequested();
            if (!path.Contains(child.Id) && !visited.Contains(child.Id))
                descendants.Add(BuildNode(child, children, visited, new HashSet<long>(path), ct));
        }

        return new CategoryAdminTreeNodeDto(row.Id, row.ParentId, row.Name, row.Slug,
            row.Description, row.DisplayOrder, row.Status, descendants);
    }

    private static CategoryAdminTreeNodeDto? FindNode(
        IReadOnlyList<CategoryAdminTreeNodeDto> hierarchy,
        long categoryId,
        CancellationToken ct)
    {
        var pending = new Queue<CategoryAdminTreeNodeDto>(hierarchy);
        var visited = new HashSet<long>();
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var node = pending.Dequeue();
            if (!visited.Add(node.Id)) continue;
            if (node.Id == categoryId) return node;
            foreach (var child in node.Children) pending.Enqueue(child);
        }

        return null;
    }

    private static IQueryable<Category> ApplyFilter(
        IQueryable<Category> query,
        CategoryAdminFilterDto? filter,
        bool isInMemory)
    {
        if (!string.IsNullOrWhiteSpace(filter?.Name))
        {
            var normalizedName = filter.Name.Trim();
            query = isInMemory
                ? query.Where(row => row.Name.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
                : query.Where(row => row.Name.Contains(normalizedName));
        }
        return filter?.Status is { } status
            ? query.Where(row => row.Status == status)
            : query;
    }

    private static Error NotFound() => new("CATEGORY_NOT_FOUND", "The category was not found.");
}
