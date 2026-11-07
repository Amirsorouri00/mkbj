using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Modules.Admin.Application.Ports.Inbound;
using EwaysVNext.Modules.Admin.Application.Views;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Admin.Adapter.Inbound.GraphQL;

/// <summary>
/// The composed admin views on the one shared schema.
/// </summary>
/// <remarks>
/// <para>
/// The same services the REST routes call, so the two surfaces cannot drift: an operator reading
/// the dashboard over GraphQL and one reading it over REST are looking at the same composition,
/// not two implementations of it.
/// </para>
/// <para>
/// <c>[Authorize]</c> then <c>[RequirePermission]</c>, in that order, on <b>each</b> resolver.
/// Never on the class — a type-level attribute on an <c>[ExtendObjectType]</c> merges onto the
/// whole <c>Query</c> and would gate every other module's fields, including the anonymous
/// storefront ones. <c>GraphQLGateScopeTests</c> fails the build on that.
/// </para>
/// </remarks>
[ExtendObjectType(typeof(Query))]
public sealed class AdminCompositionQueries
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.OrdersManage)]
    public Task<AdminDashboard> AdminDashboardAsync(
        [Service] IAdminDashboardService dashboard, CancellationToken ct = default) =>
        dashboard.GetAsync(ct);

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CustomersManage)]
    public async Task<Customer360> AdminCustomer360Async(
        long customerId,
        [Service] ICustomer360Service customers,
        int recentOrders = 20,
        CancellationToken ct = default) =>
        (await customers.GetAsync(customerId, recentOrders, ct)).ValueOrThrow();

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.OrdersManage)]
    public async Task<Order360> AdminOrder360Async(
        long orderId, [Service] IOrder360Service orders, CancellationToken ct = default) =>
        (await orders.GetAsync(orderId, ct)).ValueOrThrow();

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.AuditVerify)]
    public Task<AuditVerificationView> AdminAuditVerificationAsync(
        [Service] IAuditVerificationService audit, CancellationToken ct = default) =>
        audit.VerifyAsync(ct);
}
