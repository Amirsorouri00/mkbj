namespace EwaysVNext.Queries.Application.Paginations;

public abstract record CursorPaginationQuery
{
    public long? PreviousCursor { get; init; }
    public long? NextCursor { get; init; }
    public int PageSize { get; init; } = 20;
    public string? Filter { get; init; }
}
