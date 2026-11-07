using System.Security.Claims;
using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Sales.Application;
using EwaysVNext.Modules.Sales.Application.Money;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.QueryApplication.Admin;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.GraphQL.Admin;

public sealed record ConfirmOrderPaymentInput(long OrderId, string Method, string? Reference);

public sealed record CancelOrderInput(long OrderId);

public sealed record RefundOrderInput(
    long OrderId, string Amount, string Reason, string? ExternalReference);

/// <summary>
/// The administrator's acts on an order, over GraphQL.
/// </summary>
/// <remarks>
/// <para>
/// Each mutation returns the order reloaded through the read side rather than a shape built
/// from the aggregate it just changed. Two ways of describing one order drift, and the drift
/// shows up as an administrator refreshing the page and seeing something different.
/// </para>
/// <para>
/// <c>[Authorize]</c> then <c>[RequirePermission]</c>, in that order, on each resolver — never
/// on the class, where it would merge onto the whole <c>Mutation</c> type and gate every other
/// module's fields.
/// </para>
/// </remarks>
[ExtendObjectType(typeof(Mutation))]
public sealed class AdminOrderMutations
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.OrdersManage)]
    public async Task<AdminOrderDetailDto> ConfirmOrderPaymentAsync(
        ConfirmOrderPaymentInput input,
        ClaimsPrincipal user,
        [Service] IOrderAdminService service,
        [Service] ISalesAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        (await service.ConfirmExternalPaymentAsync(
            input.OrderId, input.Method, input.Reference, Actor(user), ct)).ValueOrThrow(0);

        return (await queries.GetOrderAsync(input.OrderId, ct)).ValueOrThrow();
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.OrdersManage)]
    public async Task<AdminOrderDetailDto> CancelOrderAsync(
        CancelOrderInput input,
        ClaimsPrincipal user,
        [Service] IOrderAdminService service,
        [Service] ISalesAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        (await service.CancelAsync(input.OrderId, Actor(user), ct)).ValueOrThrow(0);

        return (await queries.GetOrderAsync(input.OrderId, ct)).ValueOrThrow();
    }

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.RefundsManage)]
    public async Task<AdminOrderDetailDto> RefundOrderAsync(
        RefundOrderInput input,
        ClaimsPrincipal user,
        [Service] IOrderAdminService service,
        [Service] ISalesAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!AmountText.TryParseMinor(input.Amount, out var minor))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("amount must be a decimal amount with two places.")
                    .SetCode("VALIDATION_FAILED")
                    .Build());
        }

        (await service.RecordRefundAsync(
            input.OrderId, minor, input.Reason, input.ExternalReference, Actor(user), ct))
            .ValueOrThrow();

        return (await queries.GetOrderAsync(input.OrderId, ct)).ValueOrThrow();
    }

    private static AdminActor Actor(ClaimsPrincipal user) =>
        AdminActor.TryFrom(user, out var actor)
            ? actor!
            : throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage(SalesErrors.InsufficientScope.Message)
                    .SetCode("FORBIDDEN")
                    .Build());
}

/// <summary>Reading orders across both channels.</summary>
[ExtendObjectType(typeof(Query))]
public sealed class AdminOrderQueries
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.OrdersManage)]
    public async Task<CursorPage<AdminOrderListItemDto>> AdminOrdersAsync(
        [Service] ISalesAdminQueries queries,
        AdminOrderFilter? filter = null,
        CancellationToken ct = default) =>
        (await queries.ListOrdersAsync(filter ?? new AdminOrderFilter(), ct)).ValueOrThrow();

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.OrdersManage)]
    public async Task<AdminOrderDetailDto> AdminOrderAsync(
        long id, [Service] ISalesAdminQueries queries, CancellationToken ct = default) =>
        (await queries.GetOrderAsync(id, ct)).ValueOrThrow();
}
