using System.Globalization;
using System.Security.Claims;

using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Inbound;
using EwaysVNext.Modules.Catalog.QueryApplication.Products;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Admin;

public sealed record CreateBrandRequest(string DisplayName, IReadOnlyList<string>? Aliases);

public sealed record RenameBrandRequest(string DisplayName);

public sealed record SetBrandAliasesRequest(IReadOnlyList<string> Aliases);

public sealed record SetBrandStatusRequest(bool IsActive);

public sealed class BrandAdminEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/admin/catalog/brands";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Staff, not super admin: brands are everyday catalogue work, unlike partners or FX.
        var group = app.MapGroup(BasePath)
            .WithTags("Catalog administration")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.CatalogManage);

        group.MapGet("/", async (
            string? q, int? page, int? pageSize,
            ICatalogAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.ListBrandsAsync(q, page ?? 1, pageSize ?? 50, ct)).ToHttpResult());

        group.MapPost("/", async (
            CreateBrandRequest request, ClaimsPrincipal user,
            IBrandAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor))
                return EndpointResult.Forbidden();

            var result = await service.CreateAsync(
                request.DisplayName, request.Aliases ?? [], actor!, ct);

            return result.ToCreatedHttpResult(
                id => $"{BasePath}/{id.ToString(CultureInfo.InvariantCulture)}");
        });

        group.MapPut("/{id:long}", (
            long id, RenameBrandRequest request, ClaimsPrincipal user,
            IBrandAdminService service, CancellationToken ct) =>
            Act(user, actor => service.RenameAsync(id, request.DisplayName, actor, ct)));

        group.MapPut("/{id:long}/aliases", (
            long id, SetBrandAliasesRequest request, ClaimsPrincipal user,
            IBrandAdminService service, CancellationToken ct) =>
            Act(user, actor => service.SetAliasesAsync(id, request.Aliases ?? [], actor, ct)));

        group.MapPut("/{id:long}/status", (
            long id, SetBrandStatusRequest request, ClaimsPrincipal user,
            IBrandAdminService service, CancellationToken ct) =>
            Act(user, actor => service.SetActiveAsync(id, request.IsActive, actor, ct)));
    }

    private static async Task<IResult> Act(ClaimsPrincipal user, Func<AdminActor, Task<Result>> action)
    {
        if (!AdminActor.TryFrom(user, out var actor))
            return EndpointResult.Forbidden();

        return (await action(actor!)).ToHttpResult();
    }
}
