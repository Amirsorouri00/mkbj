using System.Globalization;

using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Modules.Catalog.Application.Categories.Commands;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Inbound;
using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Admin;

public sealed record CreateCategoryRequest(
    string Name, string Slug, string? Description, long? ParentId, int DisplayOrder);

public sealed record UpdateCategoryRequest(
    string Name, string Slug, string? Description, int DisplayOrder);

public sealed record SetCategoryStatusRequest(bool IsActive);

public sealed record MoveCategoryRequest(long? ParentId, int DisplayOrder);

public sealed record SetCategoryAliasesRequest(IReadOnlyList<string> Aliases);

/// <summary>
/// The category admin surface in REST, alongside the GraphQL one it mirrors — every front-end
/// API is available in both.
/// </summary>
public sealed class CategoryAdminEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/admin/catalog/categories";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath)
            .WithTags("Catalog administration")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.CatalogManage);

        group.MapGet("/tree", async (ICategoryAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.GetHierarchyAsync(ct)).ToHttpResult());

        group.MapGet("/{id:long}", async (
            long id, ICategoryAdminQueries queries, CancellationToken ct) =>
            (await queries.GetByIdAsync(id, ct)).ToHttpResult());

        group.MapPost("/", async (
            CreateCategoryRequest request, ICategoryService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(
                new CreateCategoryCommand(
                    request.Name, request.Slug, request.Description, request.ParentId, request.DisplayOrder),
                ct);

            return result.ToCreatedHttpResult(
                id => $"{BasePath}/{id.ToString(CultureInfo.InvariantCulture)}");
        });

        group.MapPut("/{id:long}", async (
            long id, UpdateCategoryRequest request, ICategoryService service, CancellationToken ct) =>
            (await service.UpdateAsync(
                new UpdateCategoryCommand(
                    id, request.Name, request.Slug, request.Description, request.DisplayOrder, null),
                ct)).ToHttpResult());

        group.MapPut("/{id:long}/status", async (
            long id, SetCategoryStatusRequest request, ICategoryService service, CancellationToken ct) =>
            (await service.SetStatusAsync(
                new SetCategoryStatusCommand(
                    id, request.IsActive ? CategoryStatus.Active : CategoryStatus.Inactive),
                ct)).ToHttpResult());

        group.MapPut("/{id:long}/move", async (
            long id, MoveCategoryRequest request, ICategoryService service, CancellationToken ct) =>
            (await service.MoveAsync(
                new MoveCategoryCommand(id, request.ParentId, request.DisplayOrder), ct)).ToHttpResult());

        group.MapPut("/{id:long}/aliases", async (
            long id, SetCategoryAliasesRequest request, ICategoryService service, CancellationToken ct) =>
            (await service.SetAliasesAsync(id, request.Aliases ?? [], ct)).ToHttpResult());
    }
}
