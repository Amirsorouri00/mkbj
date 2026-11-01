using System.Globalization;
using System.Security.Claims;

using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.QueryApplication.Products;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Admin;

public sealed class CatalogAdminEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/admin/catalog/products";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath)
            .WithTags("Catalog administration")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.CatalogManage);

        group.MapGet("/", async (
            string? q, string? status, long? brandId, long? categoryId, int? page, int? pageSize,
            ICatalogAdminQueries queries, CancellationToken ct) =>
        {
            if (!TryStatus(status, out var parsed))
                return Result.Failure(UnknownStatus(status)).ToHttpResult();

            var filter = new ProductAdminFilter(q, parsed, brandId, categoryId);
            return Result.Success(
                await queries.ListProductsAsync(filter, page ?? 1, pageSize ?? 50, ct)).ToHttpResult();
        });

        group.MapGet("/{id:long}", async (long id, ICatalogAdminQueries queries, CancellationToken ct) =>
            (await queries.GetProductAsync(id, ct)).ToHttpResult());

        group.MapPost("/", async (
            CreateProductRequest request, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor))
                return EndpointResult.Forbidden();

            var result = await service.CreateProductAsync(
                new CreateProductCommand(
                    request.BrandId, request.PrimaryCategoryId, request.Title,
                    request.Slug, request.ShortDescription, request.Description),
                actor!, ct);

            return result.ToCreatedHttpResult(id => $"{BasePath}/{Id(id)}");
        });

        group.MapPut("/{id:long}", (
            long id, UpdateProductRequest request, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
            Act(user, actor => service.UpdateProductAsync(
                new UpdateProductCommand(
                    id, request.BrandId, request.PrimaryCategoryId, request.Title,
                    request.ShortDescription, request.Description),
                actor, ct)));

        group.MapPut("/{id:long}/secondary-categories", (
            long id, SetSecondaryCategoriesRequest request, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
            Act(user, actor => service.SetSecondaryCategoriesAsync(
                id, request.CategoryIds ?? [], actor, ct)));

        group.MapPost("/{id:long}/variants", async (
            long id, AddVariantRequest request, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor))
                return EndpointResult.Forbidden();

            var result = await service.AddVariantAsync(
                new AddVariantCommand(
                    id, request.Sku, request.Attributes ?? new Dictionary<string, string>()),
                actor!, ct);

            return result.ToCreatedHttpResult(variantId => $"{BasePath}/{Id(id)}/variants/{Id(variantId)}");
        });

        group.MapPut("/{id:long}/variants/{variantId:long}/status", (
            long id, long variantId, SetActiveRequest request, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
            Act(user, actor => service.SetVariantActiveAsync(id, variantId, request.IsActive, actor, ct)));

        group.MapPost("/{id:long}/identifiers", (
            long id, AddIdentifierRequest request, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
            Act(user, actor => service.AddIdentifierAsync(
                id, new IdentifierInput(request.Type, request.Value, request.SourceDomain), actor, ct)));

        group.MapDelete("/{id:long}/identifiers/{identifierId:long}", (
            long id, long identifierId, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
            Act(user, actor => service.RemoveIdentifierAsync(id, identifierId, actor, ct)));

        group.MapPut("/{id:long}/status", (
            long id, SetActiveRequest request, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
            Act(user, actor => service.SetProductActiveAsync(id, request.IsActive, actor, ct)));

        group.MapPost("/{id:long}/media", async (
            long id, HttpRequest http, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor))
                return EndpointResult.Forbidden();

            if (!http.HasFormContentType)
                return EndpointResult.BadRequest("Send the image as multipart/form-data.");

            var form = await http.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null)
                return EndpointResult.BadRequest("The form must contain a 'file' part.");

            var rightsText = form["rights"].ToString();
            if (!TryRights(rightsText, out var rights))
                return Result.Failure(UnknownRights(rightsText)).ToHttpResult();

            await using var content = file.OpenReadStream();
            var sortOrder = int.TryParse(form["sortOrder"].ToString(), CultureInfo.InvariantCulture, out var order)
                ? order
                : (int?)null;

            var result = await service.UploadMediaAsync(
                new UploadMediaCommand(id, content, file.Length, form["alt"].ToString(), rights, sortOrder),
                actor!, ct);

            return result.ToCreatedHttpResult(mediaId => $"{BasePath}/{Id(id)}/media/{Id(mediaId)}");
        })
        // The admin API is token-authenticated and has no cookies, so the antiforgery filter
        // ASP.NET adds to form posts has nothing to validate and would reject every upload.
        .DisableAntiforgery();

        group.MapDelete("/{id:long}/media/{mediaId:long}", (
            long id, long mediaId, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
            Act(user, actor => service.RemoveMediaAsync(id, mediaId, actor, ct)));

        group.MapPost("/bulk", async (
            BulkProductRequest request, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor))
                return EndpointResult.Forbidden();

            if (!TryBulkStatus(request.Status, out var status))
                return Result.Failure(UnknownStatus(request.Status)).ToHttpResult();

            var result = await service.BulkEditAsync(
                new BulkProductEdit(
                    request.ProductIds ?? [], status, request.PrimaryCategoryId, request.BrandId),
                actor!, ct);

            return result.ToHttpResult();
        });

        group.MapPost("/{id:long}/merge-into/{survivorId:long}", (
            long id, long survivorId, ClaimsPrincipal user,
            ICatalogAdminService service, CancellationToken ct) =>
            Act(user, actor => service.MergeDuplicateAsync(id, survivorId, actor, ct)));
    }

    private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Parsed by name, case-insensitively, and an unrecognised value is refused rather than
    /// ignored: silently dropping a filter shows the admin a list that is not what they asked for.
    /// </summary>
    private static bool TryStatus(string? text, out ProductStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        if (!Enum.TryParse<ProductStatus>(text, ignoreCase: true, out var parsed)) return false;

        status = parsed;
        return true;
    }

    private static bool TryBulkStatus(string? text, out BulkStatusChange status)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            status = BulkStatusChange.None;
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out status);
    }

    private static bool TryRights(string? text, out MediaUsageRights rights)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            // The safe default for an upload that does not say: an unconfirmed image cannot make
            // a product sellable, which is the only thing this value gates.
            rights = MediaUsageRights.RightsUnknown;
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out rights);
    }

    private static Error UnknownStatus(string? text) =>
        new("VALIDATION_FAILED", $"'{text}' is not a product status.");

    private static Error UnknownRights(string? text) =>
        new("VALIDATION_FAILED", $"'{text}' is not a media usage-rights value.");

    private static async Task<IResult> Act(ClaimsPrincipal user, Func<AdminActor, Task<Result>> action)
    {
        if (!AdminActor.TryFrom(user, out var actor))
            return EndpointResult.Forbidden();

        return (await action(actor!)).ToHttpResult();
    }
}
