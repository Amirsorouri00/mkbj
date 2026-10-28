namespace EwaysVNext.Queries.Application.Paginations;

public sealed class PaginatedQuery<TData>(int page, int pageSize, int pageCount, int totalCount, IReadOnlyList<TData> items)
{
    public int Page { get; } = page;
    public int PageSize { get; } = pageSize;
    public int PageCount { get; } = pageCount;
    public int TotalCount { get; } = totalCount;
    public IReadOnlyList<TData> Items { get; } = items;
}
