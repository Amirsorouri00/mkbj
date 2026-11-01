using System.Security.Claims;

using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Customer.QueryApplication.Customers;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Admin;

/// <summary>
/// The backoffice view of customers. Staff, not super admin: looking a customer up and suspending
/// one are day-to-day shop work, and the operations here change no money and hand out no access.
/// </summary>
public sealed class CustomerAdminEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/customers")
            .WithTags("Customer administration")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.CustomersManage);

        // The query parameters are bound one at a time rather than through a request record.
        // A record would carry a settable PartnerId, and an architecture test forbids any bound
        // shape that does — a partner id must never arrive from the caller on a partner-facing
        // surface, and the simplest way to keep that true is for no such shape to exist.
        group.MapGet("/", async (
            string? kind, string? status, long? partnerId, string? search, int? page, int? pageSize,
            ICustomerAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.ListAsync(
                new AdminCustomerListFilter(kind, status, partnerId, search, page ?? 1, pageSize ?? 20), ct))
                .ToHttpResult());

        group.MapGet("/{id:long}", async (
            long id, ICustomerAdminQueries queries, CancellationToken ct) =>
            (await queries.GetAsync(id, ct)).ToHttpResult());

        group.MapPost("/{id:long}/suspend", (long id, ClaimsPrincipal user,
            ICustomerAdminService service, CancellationToken ct) =>
            Act(user, actor => service.SuspendAsync(id, actor, ct)));

        group.MapPost("/{id:long}/reactivate", (long id, ClaimsPrincipal user,
            ICustomerAdminService service, CancellationToken ct) =>
            Act(user, actor => service.ReactivateAsync(id, actor, ct)));
    }

    private static async Task<IResult> Act(ClaimsPrincipal user, Func<AdminActor, Task<Result>> action)
    {
        if (!AdminActor.TryFrom(user, out var actor))
            return EndpointResult.Forbidden();

        return (await action(actor!)).ToHttpResult();
    }
}
