using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using HotChocolate.Data;
using HotChocolate.Types.Relay;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Categories;

[ExtendObjectType(typeof(Query))]
#pragma warning disable CA1822
public sealed class CategoryAdminQueries
{
    //[Authorize(Policy = "Admin")]
    [UsePaging(IncludeTotalCount = true, MaxPageSize = 100)]
    [UseProjection]
    public IQueryable<CategoryAdminDto> Categories(CategoryAdminFilterDto? filter,
        [Service] ICategoryAdminQueries queries) => queries.Search(filter);

    //[Authorize(Policy = "Admin")]
    public async Task<CategoryAdminDto> CategoryAsync([ID] long id,
        [Service] ICategoryAdminQueries queries, CancellationToken ct)
    {
        var result = await queries.GetByIdAsync(id, ct);
        return ResultGraphQLExtensions.ValueOrThrow(result);
    }

    //[Authorize(Policy = "Admin")]
    public async Task<CategoryAdminDto?> CategoryParentAsync([ID] long categoryId,
        [Service] ICategoryAdminQueries queries, CancellationToken ct)
    {
        var result = await queries.GetParentAsync(categoryId, ct);
        return ResultGraphQLExtensions.ValueOrThrow(result);
    }

    //[Authorize(Policy = "Admin")]
    public async Task<IReadOnlyList<CategoryAdminTreeNodeDto>> CategoryDescendantsAsync(
        [ID] long categoryId, [Service] ICategoryAdminQueries queries, CancellationToken ct)
    {
        var result = await queries.GetDescendantsAsync(categoryId, ct);
        return ResultGraphQLExtensions.ValueOrThrow(result);
    }

    //[Authorize(Policy = "Admin")]
    public Task<IReadOnlyList<CategoryAdminDto>> CategoryChildrenAsync([ID] long? parentId,
        [Service] ICategoryAdminQueries queries, CancellationToken ct) => queries.GetChildrenAsync(parentId, ct);

    //[Authorize(Policy = "Admin")]
    public Task<IReadOnlyList<CategoryAdminTreeNodeDto>> CategoryHierarchyAsync(
        [Service] ICategoryAdminQueries queries, CancellationToken ct) =>
        queries.GetHierarchyAsync(ct);

    //[Authorize(Policy = "Admin")]
    [UsePaging(typeof(IdType), IncludeTotalCount = true, MaxPageSize = 100)]
    public async Task<IQueryable<long>> CategoryProductsAsync([ID] long categoryId,
        [Service] ICategoryAdminQueries queries, CancellationToken ct)
    {
        var result = await queries.GetProductIdsAsync(categoryId, ct);
        return ResultGraphQLExtensions.ValueOrThrow(result);
    }

    //internal static T ValueOrThrow<T>(EwaysVNext.Outcomes.Result<T> result)
    //{
    //    if (result.IsSuccess) return result.Value!;
    //    throw ToGraphQlException(result.Error);
    //}

    //internal static GraphQLException ToGraphQlException(EwaysVNext.Outcomes.Error error) => new(ErrorBuilder.New()
    //    .SetMessage(error.Message).SetCode(error.Code).SetExtension("httpStatus", error.HttpStatus).Build());
}
#pragma warning restore CA1822
