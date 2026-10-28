using System.Linq.Expressions;
using EwaysVNext.Queries.Application.Paginations;
using Gridify;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Queries.Implementation;

public static class QueryExtensions
{
    public static IQueryable<T> When<T>(this IQueryable<T> query, bool criteria, Expression<Func<T, bool>> predicate)
        => criteria ? query.Where(predicate) : query;

    public static async Task<PaginatedQuery<TDestination>> ApplyPagination<TSource, TDestination>(
        this IQueryable<TSource> query,
        PaginationQuery pagination,
        Expression<Func<TSource, TDestination>> projector,
        CancellationToken ct,
        IGridifyMapper<TSource>? mapper = null)
    {
        var filtered = mapper is not null
            ? query.ApplyFiltering(pagination.Filter, mapper)
            : query.ApplyFiltering(pagination.Filter);

        var totalCount = await filtered.CountAsync(ct);

        var ordered = mapper is not null
            ? filtered.ApplyOrdering(pagination.Order, mapper)
            : filtered.ApplyOrdering(pagination.Order);

        var items = await ordered
            .ApplyPaging(pagination.Page, pagination.PageSize)
            .Select(projector)
            .ToListAsync(ct);

        var pageCount = (int)Math.Ceiling(totalCount / (double)pagination.PageSize);
        return new PaginatedQuery<TDestination>(pagination.Page, pagination.PageSize, pageCount, totalCount, items);
    }

    public static async Task<CursorPaginatedQuery<TDestination>> ApplyCursorPagination<TSource, TDestination>(
        this IQueryable<TSource> query,
        CursorPaginationQuery pagination,
        Expression<Func<TSource, long>> idSelector,
        Expression<Func<TSource, TDestination>> projector,
        CancellationToken ct,
        IGridifyMapper<TSource>? mapper = null)
        where TDestination : ICursorResponse
    {
        var filtered = mapper is not null
            ? query.ApplyFiltering(pagination.Filter, mapper)
            : query.ApplyFiltering(pagination.Filter);

        var totalCount = await filtered.CountAsync(ct);

        var goingBackward = pagination.PreviousCursor.HasValue;
        var keyed = pagination.NextCursor.HasValue
            ? filtered.Where(BuildComparison(idSelector, pagination.NextCursor.Value, lessThan: true))
            : pagination.PreviousCursor.HasValue
                ? filtered.Where(BuildComparison(idSelector, pagination.PreviousCursor.Value, lessThan: false))
                : filtered;

        var items = await keyed
            .OrderByDescending(idSelector)
            .Take(pagination.PageSize + 1)
            .Select(projector)
            .ToListAsync(ct);

        var hasNextCursor = items.Count > pagination.PageSize;
        if (hasNextCursor) items.RemoveAt(items.Count - 1);
        if (goingBackward) items.Reverse();

        var pageCount = (int)Math.Ceiling(totalCount / (double)pagination.PageSize);
        var nextCursor = items.Count > 0 ? items[^1].Id : (long?)null;

        return new CursorPaginatedQuery<TDestination>(
            pagination.PreviousCursor, nextCursor, hasNextCursor, pagination.PageSize, pageCount, totalCount, items);
    }

    private static Expression<Func<TSource, bool>> BuildComparison<TSource>(
        Expression<Func<TSource, long>> idSelector, long cursor, bool lessThan)
    {
        var body = lessThan
            ? Expression.LessThan(idSelector.Body, Expression.Constant(cursor))
            : Expression.GreaterThan(idSelector.Body, Expression.Constant(cursor));
        return Expression.Lambda<Func<TSource, bool>>(body, idSelector.Parameters[0]);
    }
}
