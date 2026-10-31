using System.Globalization;
using System.Security.Claims;

using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Modules.Partners.QueryApplication;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Partners.Adapter.Inbound.Rest.Admin;

/// <summary>
/// Partner administration over REST.
/// <para>
/// Gated twice, as every admin surface is: the realm policy says the caller is a backoffice super
/// admin, and the permission says that role may manage partners. The permission rows are editable
/// data; the realm policy is not, so it bounds what a misedited row can reach.
/// </para>
/// </summary>
public sealed class PartnerAdminEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/admin/partners";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath)
            .WithTags("Partner administration")
            .RequireAuthorization(AuthPolicies.AdminSuper)
            .RequirePermission(AdminPermissions.PartnersManage);

        group.MapPost("/", async (
            CreatePartnerRequest request, ClaimsPrincipal user,
            IPartnerAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor))
                return EndpointResult.Forbidden();

            var result = await service.CreateAsync(
                new CreatePartnerCommand(request.Name, request.Scopes, request.RateLimitPerMinute, request.BulkMaxItems),
                actor!, ct);

            return result.ToCreatedHttpResult(id => $"{BasePath}/{id.ToString(CultureInfo.InvariantCulture)}");
        });

        group.MapGet("/", async (
            int? page, int? pageSize, IPartnerAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.ListAsync(page ?? 1, pageSize ?? 20, ct)).ToHttpResult());

        group.MapGet("/{id:long}", async (
            long id, IPartnerAdminQueries queries, IClock clock, CancellationToken ct) =>
            (await queries.GetAsync(id, clock.UtcNow, ct)).ToHttpResult());

        group.MapPost("/{id:long}/suspend", (long id, ClaimsPrincipal user,
            IPartnerAdminService service, CancellationToken ct) =>
            Act(user, actor => service.SuspendAsync(id, actor, ct)));

        group.MapPost("/{id:long}/reactivate", (long id, ClaimsPrincipal user,
            IPartnerAdminService service, CancellationToken ct) =>
            Act(user, actor => service.ReactivateAsync(id, actor, ct)));

        group.MapPut("/{id:long}/scopes", (long id, SetScopesRequest request, ClaimsPrincipal user,
            IPartnerAdminService service, CancellationToken ct) =>
            Act(user, actor => service.SetScopesAsync(id, request.Scopes, actor, ct)));

        group.MapPut("/{id:long}/limits", (long id, SetLimitsRequest request, ClaimsPrincipal user,
            IPartnerAdminService service, CancellationToken ct) =>
            Act(user, actor => service.SetLimitsAsync(
                id, request.RateLimitPerMinute, request.BulkMaxItems, actor, ct)));

        group.MapPost("/{id:long}/keys", async (
            long id, IssueKeyRequest request, ClaimsPrincipal user, HttpContext context,
            IPartnerAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor))
                return EndpointResult.Forbidden();

            var result = await service.IssueKeyAsync(id, request.Scopes, request.ExpiresAt, actor!, ct);

            return Issued(context, result, id);
        });

        group.MapPost("/{id:long}/keys/{keyId:long}/rotate", async (
            long id, long keyId, RotateKeyRequest? request, ClaimsPrincipal user, HttpContext context,
            IPartnerAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor))
                return EndpointResult.Forbidden();

            var overlap = request?.OverlapHours is { } hours
                ? TimeSpan.FromHours(hours)
                : (TimeSpan?)null;

            var result = await service.RotateKeyAsync(id, keyId, overlap, actor!, ct);

            return Issued(context, result, id);
        });

        group.MapPost("/{id:long}/keys/{keyId:long}/revoke", (long id, long keyId, ClaimsPrincipal user,
            IPartnerAdminService service, CancellationToken ct) =>
            Act(user, actor => service.RevokeKeyAsync(id, keyId, actor, ct)));

        group.MapGet("/{id:long}/usage", async (
            long id, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
            IPartnerAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.GetUsageAsync(id, from, to, page ?? 1, pageSize ?? 20, ct))
                .ToHttpResult());
    }

    /// <summary>
    /// The plaintext key exists in exactly one response and is never stored, so the response must
    /// not be cached anywhere on the way back to the admin who asked for it.
    /// </summary>
    private static IResult Issued(HttpContext context, Result<IssuedApiKey> result, long partnerId)
    {
        if (result.IsSuccess)
            context.Response.Headers.CacheControl = "no-store";

        return result.ToCreatedHttpResult(key =>
            $"{BasePath}/{partnerId.ToString(CultureInfo.InvariantCulture)}/keys/{key.KeyId.ToString(CultureInfo.InvariantCulture)}");
    }

    private static async Task<IResult> Act(ClaimsPrincipal user, Func<AdminActor, Task<Result>> action)
    {
        if (!AdminActor.TryFrom(user, out var actor))
            return EndpointResult.Forbidden();

        return (await action(actor!)).ToHttpResult();
    }
}
