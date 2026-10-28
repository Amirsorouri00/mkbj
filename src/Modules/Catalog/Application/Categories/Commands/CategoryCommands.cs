using EwaysVNext.Modules.Catalog.Contracts.Categories;

namespace EwaysVNext.Modules.Catalog.Application.Categories.Commands;

public sealed record CreateCategoryCommand(
    string Name, string Slug, string? Description, long? ParentId, int DisplayOrder);
public sealed record UpdateCategoryCommand(
    long CategoryId, string Name, string Slug, string? Description, int DisplayOrder,
    CategoryStatus? Status);
public sealed record SetCategoryStatusCommand(long CategoryId, CategoryStatus Status);
public sealed record MoveCategoryCommand(long CategoryId, long? ParentId, int DisplayOrder);
public sealed record BulkDeactivateCategoriesCommand(IReadOnlyCollection<long> CategoryIds);
public sealed record DeleteCategoryCommand(long CategoryId);
public sealed record AssignCategoryProductCommand(
    long CategoryId, long ProductId, Guid? AssignedByUserId);
public sealed record UnassignCategoryProductCommand(long CategoryId, long ProductId);
