using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Categories;

public sealed class CategoryPublicEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Categories");

        group.MapGet("/categories/tree", async (ICategoryQueries queries, CancellationToken ct) =>
            Result.Success(await queries.GetTreeAsync(ct)).ToHttpResult());

        group.MapGet("/categories/{id:long}", async (long id, ICategoryQueries queries,
            CancellationToken ct) =>
        {
            var result = await queries.GetByIdAsync(id, ct);
            return result.ToHttpResult();
        });

        group.MapGet("/categories/{id:long}/products", async (long id, int? page, int? pageSize,
            ICategoryQueries queries, CancellationToken ct) =>
        {
            var effectivePage = page ?? 1;
            var effectivePageSize = pageSize ?? 20;
            if (effectivePage < 1)
                return Result.Failure(new Error(
                    "CATEGORY_PAGE_INVALID", "Page must be at least 1.")).ToHttpResult();
            if (effectivePageSize is < 1 or > 100)
                return Result.Failure(new Error(
                    "CATEGORY_PAGE_SIZE_INVALID", "Page size must be between 1 and 100.")).ToHttpResult();

            var result = await queries.GetProductIdsAsync(id, effectivePage, effectivePageSize, ct);
            return result.ToHttpResult();
        });

        group.MapGet("/products/{productId:long}/categories", async (long productId,
            ICategoryQueries queries, CancellationToken ct) =>
            Result.Success(await queries.GetByProductIdAsync(productId, ct)).ToHttpResult());
    }
}

public static class DependencyInjection
{
    public static IEndpointRouteBuilder MapCategoryPublicEndpoints(this IEndpointRouteBuilder app)
    {
        CategoryPublicEndpoints.Map(app);
        return app;
    }
}
