using Mkbj.Database;
using Mkbj.Modules.Catalog.Contracts.Categories;
using Microsoft.EntityFrameworkCore;

namespace Mkbj.Modules.Catalog.QueryApplication.Categories;

/// <summary>
/// The composable EF query shapes used by the public query service. Keeping these expressions
/// in one place lets persistence-contract tests inspect the exact production SQL, rather than
/// maintaining a second approximation of the read path.
/// </summary>
internal static class CategoryQueryShapes
{
    internal static IQueryable<CategoryProjection> Categories(MkbjDbContext db) =>
        db.Categories
            .AsNoTracking()
            .Select(category => new CategoryProjection(
                category.Id, category.ParentId, category.Name, category.Slug.Value,
                category.Description, category.DisplayOrder, category.Status));

    internal static IQueryable<CategoryProjection> CategoryById(
        MkbjDbContext db, long categoryId) =>
        db.Categories
            .AsNoTracking()
            .Where(category => category.Id == categoryId)
            .Select(category => new CategoryProjection(
                category.Id, category.ParentId, category.Name, category.Slug.Value,
                category.Description, category.DisplayOrder, category.Status));

    internal static IQueryable<long> ProductIdsForCategory(
        MkbjDbContext db, long categoryId) =>
        db.CategoryProducts
            .AsNoTracking()
            .Where(assignment => assignment.CategoryId == categoryId)
            .Select(assignment => assignment.ProductId);

    internal static IQueryable<long> PagedProductIdsForCategory(
        MkbjDbContext db, long categoryId, int skip, int take) =>
        ProductIdsForCategory(db, categoryId)
            .OrderBy(productId => productId)
            .Skip(skip)
            .Take(take);

    internal static IQueryable<CategoryProjection> ProductCategoriesForProduct(
        MkbjDbContext db, long productId) =>
        db.CategoryProducts
            .AsNoTracking()
            .Where(assignment => assignment.ProductId == productId)
            .Join(
                db.Categories.AsNoTracking(),
                assignment => assignment.CategoryId,
                category => category.Id,
                (_, category) => category)
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Id)
            .Select(category => new CategoryProjection(
                category.Id, category.ParentId, category.Name, category.Slug.Value,
                category.Description, category.DisplayOrder, category.Status));

    internal sealed record CategoryProjection(
        long Id, long? ParentId, string Name, string Slug, string? Description,
        int DisplayOrder, CategoryStatus Status);
}
