namespace EwaysVNext.Queries.Application.Paginations;

public sealed class CursorPaginatedQuery<TData>(
    long? previousCursor, long? nextCursor, bool hasNextCursor, int pageSize, int pageCount, int totalCount, IReadOnlyList<TData> items)
{
    public long? PreviousCursor { get; } = previousCursor;
    public long? NextCursor { get; } = nextCursor;
    public bool HasNextCursor { get; } = hasNextCursor;
    public int PageSize { get; } = pageSize;
    public int PageCount { get; } = pageCount;
    public int TotalCount { get; } = totalCount;
    public IReadOnlyList<TData> Items { get; } = items;
}
