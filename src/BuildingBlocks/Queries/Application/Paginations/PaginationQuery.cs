namespace EwaysVNext.Queries.Application.Paginations;

public abstract record PaginationQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Filter { get; init; }
    public string Order { get; init; } = "id desc";
}
