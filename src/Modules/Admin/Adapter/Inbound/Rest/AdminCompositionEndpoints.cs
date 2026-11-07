using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Modules.Admin.Application.Ports.Inbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Admin.Adapter.Inbound.Rest;

/// <summary>
/// The backoffice's composed views: the dashboard, customer 360 and order 360.
/// </summary>
/// <remarks>
/// <para>
/// Each route is one call to one composition service. Admin owns no data, so there is nothing
/// here to validate beyond the ids in the path — the rules belong to the modules being composed.
/// </para>
/// <para>
/// The order 360 is a separate route from Sales' own <c>GET /api/v1/admin/orders/{id}</c>, not a
/// replacement for it. Sales answers "what is this order"; this answers "who bought it and what
/// paperwork exists", which is what an operator with a customer on the phone is actually asking.
/// </para>
/// </remarks>
public sealed class AdminCompositionEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.AdminStaff);

        admin.MapGet("/dashboard", async (
            IAdminDashboardService dashboard, CancellationToken ct) =>
            Result.Success(await dashboard.GetAsync(ct)).ToHttpResult())
            .RequirePermission(AdminPermissions.OrdersManage);

        admin.MapGet("/customers/{id:long}/360", async (
            long id, ICustomer360Service customers, CancellationToken ct, int recentOrders = 20) =>
            (await customers.GetAsync(id, recentOrders, ct)).ToHttpResult())
            .RequirePermission(AdminPermissions.CustomersManage);

        admin.MapGet("/orders/{id:long}/360", async (
            long id, IOrder360Service orders, CancellationToken ct) =>
            (await orders.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(AdminPermissions.OrdersManage);
    }
}

/// <summary>
/// Whether the audit log can still be trusted.
/// </summary>
/// <remarks>
/// Super admin only, and its own permission. Verification is cheap to read and expensive to
/// misunderstand: an operator who sees "intact: false" is looking at evidence that somebody
/// edited the record of who did what, and that is not a staff-level conversation.
/// </remarks>
public sealed class AuditVerificationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/admin/audit/verify", async (
            IAuditVerificationService audit, CancellationToken ct) =>
            Result.Success(await audit.VerifyAsync(ct)).ToHttpResult())
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.AdminSuper)
            .RequirePermission(AdminPermissions.AuditVerify);
}
