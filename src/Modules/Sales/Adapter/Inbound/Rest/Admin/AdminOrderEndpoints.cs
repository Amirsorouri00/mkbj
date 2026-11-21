using System.Security.Claims;
using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using Mkbj.Core.Application;
using Mkbj.Modules.Sales.Application;
using Mkbj.Modules.Sales.Application.Money;
using Mkbj.Modules.Sales.Application.Ports.Inbound;
using Mkbj.Modules.Sales.Application.Ports.Outbound;
using Mkbj.Modules.Sales.QueryApplication.Admin;
using Mkbj.Outcomes;
using Mkbj.Presentation.Endpoint;
using Mkbj.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mkbj.Modules.Sales.Adapter.Inbound.Rest.Admin;

public sealed record ConfirmPaymentRequest(string? Method, string? Reference);

/// <remarks>
/// The amount is a two-decimal string, never a JSON number: this is money leaving the business,
/// and a double would round it.
/// </remarks>
public sealed record RecordRefundRequest(
    string? Amount, string? Reason, string? ExternalReference);

/// <summary>
/// What an administrator may do to an order.
/// </summary>
/// <remarks>
/// Gated twice, as every admin surface is: the realm policy bounds what a misedited permission
/// row can reach. Refunds take the super-admin policy and the refunds permission, and the
/// service checks the actor again — a route is one mapping mistake away from being wrong, and
/// this is the operation that moves money out of the business.
/// </remarks>
public sealed class AdminOrderEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/admin/orders";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup(BasePath)
            .WithTags("Order administration")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.OrdersManage);

        staff.MapGet("/", async (
            ISalesAdminQueries queries, CancellationToken ct,
            string? channel = null, string? orderStatus = null, string? paymentStatus = null,
            string? currency = null, long? customerId = null, long? partnerId = null,
            string? externalOrderId = null, string? number = null,
            DateTimeOffset? from = null, DateTimeOffset? to = null,
            long? cursor = null, int pageSize = 50) =>
        {
            AdminChannelFilter? parsed = Enum.TryParse<AdminChannelFilter>(channel, true, out var value)
                ? value
                : null;

            return (await queries.ListOrdersAsync(
                new AdminOrderFilter(
                    parsed, orderStatus, paymentStatus, currency, customerId, partnerId,
                    externalOrderId, number, from, to, cursor, pageSize),
                ct)).ToHttpResult();
        });

        staff.MapGet("/{id:long}", async (
            long id, ISalesAdminQueries queries, CancellationToken ct) =>
            (await queries.GetOrderAsync(id, ct)).ToHttpResult());

        staff.MapPost("/{id:long}/confirm-payment", (
            long id, ConfirmPaymentRequest request, ClaimsPrincipal user,
            IOrderAdminService service, CancellationToken ct) =>
            Act(user, actor => service.ConfirmExternalPaymentAsync(
                id, request.Method ?? string.Empty, request.Reference, actor, ct)));

        staff.MapPost("/{id:long}/cancel", (
            long id, ClaimsPrincipal user, IOrderAdminService service, CancellationToken ct) =>
            Act(user, actor => service.CancelAsync(id, actor, ct)));

        // Money going back is a super admin's decision alone.
        var super = app.MapGroup(BasePath)
            .WithTags("Order administration")
            .RequireAuthorization(AuthPolicies.AdminSuper)
            .RequirePermission(AdminPermissions.RefundsManage);

        super.MapPost("/{id:long}/refunds", async (
            long id, RecordRefundRequest request, ClaimsPrincipal user,
            IOrderAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor)) return EndpointResult.Forbidden();

            if (!AmountText.TryParseMinor(request.Amount, out var minor))
            {
                return EndpointResult.Fail(SalesErrors.ValidationFailed(
                    "amount must be a decimal amount with two places, such as \"12.00\"."));
            }

            return (await service.RecordRefundAsync(
                id, minor, request.Reason ?? string.Empty, request.ExternalReference, actor!, ct))
                .ToHttpResult();
        });

        // The document route sits outside the orders group: an administrator fetching a PDF is
        // reading, not managing, and it covers documents of both channels.
        app.MapGet("/api/v1/admin/documents/{id:long}/pdf", async (
            long id, IInvoiceService invoices, CancellationToken ct) =>
        {
            var pdf = await invoices.GetPdfAsync(new DocumentAccess.Admin(), id, ct);

            return pdf.IsFailure
                ? EndpointResult.Fail(pdf.Error!)
                : EndpointResult.File(pdf.Value!.Content, "application/pdf", pdf.Value.FileName);
        })
            .WithTags("Order administration")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.OrdersManage);

        // The backoffice fetches through CORS. Carrying the bytes in its authenticated JSON
        // response avoids a second redirect to object storage (which must stay private and does
        // not trust the backoffice origin). The ordinary PDF route remains useful to HTTP clients.
        app.MapGet("/api/v1/admin/documents/{id:long}/pdf-data", async (
            long id, IInvoiceService invoices, CancellationToken ct) =>
        {
            var pdf = await invoices.GetPdfAsync(new DocumentAccess.Admin(), id, ct);
            return pdf.IsFailure
                ? EndpointResult.Fail(pdf.Error!)
                : Result.Success(new
                {
                    pdf.Value!.FileName,
                    ContentBase64 = Convert.ToBase64String(pdf.Value.Content),
                }).ToHttpResult();
        })
            .WithTags("Order administration")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.OrdersManage);
    }

    private static async Task<IResult> Act(
        ClaimsPrincipal user, Func<AdminActor, Task<Result>> action)
    {
        if (!AdminActor.TryFrom(user, out var actor)) return EndpointResult.Forbidden();

        return (await action(actor!)).ToHttpResult();
    }
}
