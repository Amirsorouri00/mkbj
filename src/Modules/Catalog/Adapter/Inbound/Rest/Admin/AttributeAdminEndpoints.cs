using System.Globalization;

using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ApplicationOptionInput = EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound.AttributeBlueprintOptionInput;
using ApplicationUpdateOptionInput = EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound.UpdateAttributeBlueprintOptionInput;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Admin;

public sealed record CreateAttributeRequest(
    string Name,
    AttributeBlueprintDataType DataType,
    IReadOnlyList<AttributeOptionRequest>? Options);

public sealed record AttributeOptionRequest(long? OptionId, string Value, long? MeasurementUnitId);

public sealed record UpdateAttributeRequest(
    string Name,
    AttributeBlueprintDataType DataType,
    bool IsActive,
    IReadOnlyList<AttributeOptionRequest> Options);

public sealed record SetAttributeScopeRequest(AttributeScope Scope, long? CategoryId);

public sealed record SetAttributeAliasesRequest(IReadOnlyList<string> Aliases);

public sealed record SetAttributeUnitRequest(long? UnitId);

/// <summary>
/// The attribute admin surface in REST, alongside the GraphQL one it mirrors.
/// </summary>
public sealed class AttributeAdminEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/admin/catalog/attributes";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath)
            .WithTags("Catalog administration")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.CatalogManage);

        group.MapGet("/", async (
            int? page, int? pageSize, bool? includeDeleted,
            IAttributeBlueprintQueryService queries, CancellationToken ct) =>
            Result.Success(await queries.ListAsync(
                Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 50, 1, 100),
                includeDeleted ?? false, null, ct)).ToHttpResult());

        group.MapGet("/{id:long}", async (
            long id, IAttributeBlueprintQueryService queries, CancellationToken ct) =>
            (await queries.GetByIdAsync(id, ct)).ToHttpResult());

        group.MapPost("/", async (
            CreateAttributeRequest request, IAttributeBlueprintService service, CancellationToken ct) =>
        {
            var options = (request.Options ?? [])
                .Select(option => new ApplicationOptionInput(option.Value, option.MeasurementUnitId))
                .ToArray();

            var result = await service.CreateAsync(request.Name, request.DataType, options, ct);

            return result.ToCreatedHttpResult(
                id => $"{BasePath}/{id.ToString(CultureInfo.InvariantCulture)}");
        });

        group.MapPut("/{id:long}", async (
            long id, UpdateAttributeRequest request,
            IAttributeBlueprintService service, CancellationToken ct) =>
        {
            var options = (request.Options ?? [])
                .Select(option => new ApplicationUpdateOptionInput(
                    option.OptionId, option.Value, option.MeasurementUnitId))
                .ToArray();

            return (await service.UpdateAsync(
                id, request.Name, request.DataType, request.IsActive, options, ct)).ToHttpResult();
        });

        group.MapPut("/{id:long}/scope", async (
            long id, SetAttributeScopeRequest request,
            IAttributeBlueprintService service, CancellationToken ct) =>
            (await service.SetScopeAsync(id, request.Scope, request.CategoryId, ct)).ToHttpResult());

        group.MapPut("/{id:long}/aliases", async (
            long id, SetAttributeAliasesRequest request,
            IAttributeBlueprintService service, CancellationToken ct) =>
            (await service.SetAliasesAsync(id, request.Aliases ?? [], ct)).ToHttpResult());

        group.MapPut("/{id:long}/unit", async (
            long id, SetAttributeUnitRequest request,
            IAttributeBlueprintService service, CancellationToken ct) =>
            (await service.SetUnitAsync(id, request.UnitId, ct)).ToHttpResult());
    }
}
