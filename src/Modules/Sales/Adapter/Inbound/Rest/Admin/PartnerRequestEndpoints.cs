using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using Mkbj.Modules.Sales.QueryApplication.Admin;
using Mkbj.Outcomes;
using Mkbj.Presentation.Endpoint;
using Mkbj.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mkbj.Modules.Sales.Adapter.Inbound.Rest.Admin;

public sealed class PartnerRequestEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/partner-requests")
            .WithTags("Partner request history")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.OrdersManage);
        group.MapGet("/", async (IPartnerRequestQueries queries, CancellationToken ct,
            string? search = null, long? cursor = null) =>
            Result.Success(await queries.ListAsync(search, cursor, ct)).ToHttpResult());
        group.MapGet("/{id:long}", async (long id, IPartnerRequestQueries queries, CancellationToken ct) =>
            (await queries.GetAsync(id, ct) is { } detail
                ? Result.Success(detail)
                : Result.Failure<PartnerRequestDetail>(new Error("NOT_FOUND", "No such request.", 404))).ToHttpResult());
    }
}
