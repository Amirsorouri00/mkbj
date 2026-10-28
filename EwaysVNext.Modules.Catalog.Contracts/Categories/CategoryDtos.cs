namespace EwaysVNext.Modules.Catalog.Contracts.Categories;

public sealed record CategoryDto(
    long Id, long? ParentId, string Name, string Slug, string? Description, int DisplayOrder);

public sealed record CategoryTreeNodeDto(
    long Id, long? ParentId, string Name, string Slug, string? Description, int DisplayOrder,
    IReadOnlyList<CategoryTreeNodeDto> Children);

public sealed record CategoryProductPageDto(
    IReadOnlyList<long> ProductIds, int Page, int PageSize, int TotalCount);

public enum CategoryStatus
{
    Inactive = 0,
    Active = 1,
    Deleted = 2
}

public sealed record CategoryAdminFilterDto(string? Name, CategoryStatus? Status);

public sealed record CategoryAdminParentDto
{
    public CategoryAdminParentDto() { }

    public CategoryAdminParentDto(long id, string name) => (Id, Name) = (id, name);

    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed record CategoryAdminDto
{
    public CategoryAdminDto() { }

    public CategoryAdminDto(long id, long? parentId, string name, string slug,
        string? description, int displayOrder, CategoryStatus status) =>
        (Id, ParentId, Name, Slug, Description, DisplayOrder, Status) =
        (id, parentId, name, slug, description, displayOrder, status);

    public long Id { get; init; }
    public long? ParentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int DisplayOrder { get; init; }
    public CategoryStatus Status { get; init; }
    public CategoryAdminParentDto? Parent { get; init; }
    public int ChildCount { get; init; }
}

public sealed record CategoryAdminTreeNodeDto(
    long Id, long? ParentId, string Name, string Slug, string? Description, int DisplayOrder,
    CategoryStatus Status, IReadOnlyList<CategoryAdminTreeNodeDto> Children);
