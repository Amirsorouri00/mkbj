using System.Data.Common;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Categories;

internal sealed class CategoryProductRepository(EwaysVNextDbContext db)
    : ICategoryProductRepository
{
    public Task<bool> ExistsAsync(long categoryId, long productId, CancellationToken ct = default) =>
        db.CategoryProducts.AsNoTracking().AnyAsync(assignment =>
            assignment.CategoryId == categoryId && assignment.ProductId == productId, ct);

    public Task<bool> HasAnyForCategoryAsync(long categoryId, CancellationToken ct = default) =>
        db.CategoryProducts.AsNoTracking().AnyAsync(
            assignment => assignment.CategoryId == categoryId, ct);

    // use raw sql

    //public async Task<AssociationMutationOutcome> AddIfAbsentAsync(
    //    CategoryProduct assignment, CancellationToken ct = default)
    //{
    //    try
    //    {
    //        if (!db.Database.IsRelational()) return await AddInMemoryAsync(assignment, ct);

    //        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
    //            INSERT INTO [catalog].[CategoryProduct]
    //                ([CategoryId], [ProductId], [AssignedAt], [AssignedByUserId])
    //            SELECT {assignment.CategoryId}, {assignment.ProductId}, {assignment.AssignedAt}, {assignment.AssignedByUserId}
    //            WHERE NOT EXISTS (
    //                SELECT 1 FROM [catalog].[CategoryProduct] WITH (UPDLOCK, HOLDLOCK)
    //                WHERE [CategoryId] = {assignment.CategoryId} AND [ProductId] = {assignment.ProductId});
    //            """, ct);
    //        return rows == 1 ? AssociationMutationOutcome.Applied : AssociationMutationOutcome.AlreadyInDesiredState;
    //    }
    //    catch (Exception error) when (IsUniqueViolation(error))
    //    {
    //        return await ExistsAsync(assignment.CategoryId, assignment.ProductId, ct)
    //            ? AssociationMutationOutcome.AlreadyInDesiredState
    //            : AssociationMutationOutcome.PersistenceConflict;
    //    }
    //    catch (DbException) { return AssociationMutationOutcome.PersistenceConflict; }
    //    catch (DbUpdateException) { return AssociationMutationOutcome.PersistenceConflict; }
    //}

    public async Task<AssociationMutationOutcome> AddIfAbsentAsync(
    CategoryProduct assignment,
    CancellationToken ct = default)
    {
        if (!db.Database.IsRelational())
            return await AddInMemoryAsync(assignment, ct);

        try
        {
            db.Set<CategoryProduct>().Add(assignment);
            await db.SaveChangesAsync(ct);

            return AssociationMutationOutcome.Applied;
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            // SaveChanges failed, so prevent this entity from remaining
            // in Added state and being retried by a later SaveChanges call.
            db.Entry(assignment).State = EntityState.Detached;

            return await ExistsAsync(
                assignment.CategoryId,
                assignment.ProductId,
                ct)
                ? AssociationMutationOutcome.AlreadyInDesiredState
                : AssociationMutationOutcome.PersistenceConflict;
        }
        catch (DbUpdateException)
        {
            db.Entry(assignment).State = EntityState.Detached;
            return AssociationMutationOutcome.PersistenceConflict;
        }
        catch (DbException)
        {
            db.Entry(assignment).State = EntityState.Detached;
            return AssociationMutationOutcome.PersistenceConflict;
        }
    }

    //use raw query

    //public async Task<AssociationMutationOutcome> RemoveIfPresentAsync(long categoryId, long productId,
    //    CancellationToken ct = default)
    //{
    //    try
    //    {
    //        if (!db.Database.IsRelational()) return await RemoveInMemoryAsync(categoryId, productId, ct);

    //        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
    //            DELETE FROM [catalog].[CategoryProduct]
    //            WHERE [CategoryId] = {categoryId} AND [ProductId] = {productId};
    //            """, ct);
    //        return rows == 1 ? AssociationMutationOutcome.Applied : AssociationMutationOutcome.AlreadyInDesiredState;
    //    }
    //    catch (DbException) { return AssociationMutationOutcome.PersistenceConflict; }
    //    catch (DbUpdateException) { return AssociationMutationOutcome.PersistenceConflict; }
    //}

    public async Task<AssociationMutationOutcome> RemoveIfPresentAsync(
    long categoryId,
    long productId,
    CancellationToken ct = default)
    {
        try
        {
            if (!db.Database.IsRelational())
                return await RemoveInMemoryAsync(categoryId, productId, ct);

            var rows = await db.Set<CategoryProduct>()
                .Where(x =>
                    x.CategoryId == categoryId &&
                    x.ProductId == productId)
                .ExecuteDeleteAsync(ct);

            return rows switch
            {
                0 => AssociationMutationOutcome.AlreadyInDesiredState,
                1 => AssociationMutationOutcome.Applied,
                _ => AssociationMutationOutcome.PersistenceConflict
            };
        }
        catch (DbException)
        {
            return AssociationMutationOutcome.PersistenceConflict;
        }
        catch (DbUpdateException)
        {
            return AssociationMutationOutcome.PersistenceConflict;
        }
    }

    private async Task<AssociationMutationOutcome> AddInMemoryAsync(CategoryProduct assignment,
        CancellationToken ct)
    {
        if (await ExistsAsync(assignment.CategoryId, assignment.ProductId, ct))
            return AssociationMutationOutcome.AlreadyInDesiredState;

        await db.CategoryProducts.AddAsync(assignment, ct);
        await db.SaveChangesAsync(ct);
        return AssociationMutationOutcome.Applied;
    }

    private async Task<AssociationMutationOutcome> RemoveInMemoryAsync(long categoryId, long productId,
        CancellationToken ct)
    {
        var assignment = await db.CategoryProducts.FirstOrDefaultAsync(candidate =>
            candidate.CategoryId == categoryId && candidate.ProductId == productId, ct);
        if (assignment is null) return AssociationMutationOutcome.AlreadyInDesiredState;

        db.CategoryProducts.Remove(assignment);
        await db.SaveChangesAsync(ct);
        return AssociationMutationOutcome.Applied;
    }

    private static bool IsUniqueViolation(Exception error) =>
        error is System.Data.Common.DbException { SqlState: "23505" }
        || error.InnerException is not null && IsUniqueViolation(error.InnerException);

    public async Task AddAsync(CategoryProduct assignment, CancellationToken token)
    {
        await db.Set<CategoryProduct>().AddAsync(assignment, token);
    }
}
