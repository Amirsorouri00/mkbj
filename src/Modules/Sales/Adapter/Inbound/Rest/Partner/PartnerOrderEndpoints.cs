using System.Globalization;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Common;
using EwaysVNext.Modules.Sales.Application;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Partner;

/// <summary>
/// What a partner can do with orders.
/// </summary>
/// <remarks>
/// <para>
/// No route here takes a partner id. The authenticated key says which partner is calling, and an
/// architecture test fails the build if any request shape grows a <c>PartnerId</c> — because the
/// moment one does, every query below is one forgotten filter away from serving another
/// partner's invoices.
/// </para>
/// <para>
/// Create, replay and get all return the <b>same shape</b>, because all three reload through the
/// same query. A create that answered from the aggregate it just built would drift from what a
/// later GET returns, and a partner diffing the two would be right to complain.
/// </para>
/// </remarks>
public sealed class PartnerOrderEndpoints : IEndpoint
{
    private const string OrdersPath = "/api/v1/orders";
    private const string ExternalPrefix = "ext:";

    /// <summary>Set on a replay so a partner can tell "already had this" from "made this now".</summary>
    private const string ReplayHeader = "Idempotent-Replay";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup(OrdersPath).WithTags("Partner Orders");

        orders.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthPolicies.Partner(PartnerScopes.OrdersCreate));

        orders.MapPost("/bulk", CreateBulkAsync)
            .RequireAuthorization(AuthPolicies.Partner(PartnerScopes.OrdersCreate));

        orders.MapGet("/", ListAsync)
            .RequireAuthorization(AuthPolicies.Partner(PartnerScopes.OrdersRead));

        orders.MapGet("/{id}", GetAsync)
            .RequireAuthorization(AuthPolicies.Partner(PartnerScopes.OrdersRead));

        app.MapGet("/api/v1/customers/{id}/orders", ListForCustomerAsync)
            .WithTags("Partner Orders")
            .RequireAuthorization(AuthPolicies.Partner(PartnerScopes.CustomersRead));
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        IPartnerOrderService service,
        IPartnerOrderQueries queries,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        if (!PartnerPrincipal.TryFrom(context.User, out var principal))
            return EndpointResult.Unauthorized();

        // Read in this surface's own format rather than the application-wide one, so a partner
        // sending the documented snake_case body binds and a camelCase one is refused.
        var read = await PartnerResults.ReadAsync<CreatePartnerOrderRequest>(context, ct);
        if (read.IsFailure) return EndpointResult.Fail(read.Error!);

        var validated = PartnerContracts.Validate(read.Value!);
        if (validated.IsFailure) return EndpointResult.Fail(validated.Error!);

        var (externalOrderId, externalCustomerId, amountMinor, currency, contact) = validated.Value;
        var correlationId = CallerClaims.CorrelationIdOf(context);

        // Logged, never used. Spec §5.6: the external order id is the idempotency key, and
        // honouring a second one would give a partner two different ways to mean "the same
        // order" that could disagree with each other.
        if (context.Request.Headers.TryGetValue("Idempotency-Key", out var supplied))
        {
            loggers.CreateLogger<PartnerOrderEndpoints>().LogInformation(
                "Ignoring Idempotency-Key {Key}; external_order_id is the key.", supplied.ToString());
        }

        var created = await service.CreateAsync(
            principal!,
            new CreatePartnerOrderCommand(
                externalOrderId, externalCustomerId, amountMinor, currency, contact),
            correlationId,
            ct);

        if (created.IsFailure) return EndpointResult.Fail(created.Error!);

        var order = await queries.GetAsync(principal!, created.Value!.OrderId, ct);
        if (order.IsFailure) return EndpointResult.Fail(order.Error!);

        var body = PartnerContracts.ToResponse(
            order.Value!, correlationId, created.Value.Warnings);

        if (!created.Value.Replayed)
        {
            return PartnerResults.Created(
                $"{OrdersPath}/{created.Value.OrderId.ToString(CultureInfo.InvariantCulture)}", body);
        }

        context.Response.Headers[ReplayHeader] = "true";

        return PartnerResults.Ok(body);
    }

    /// <summary>
    /// Many orders in one call, each succeeding or failing on its own.
    /// </summary>
    /// <remarks>
    /// Always 200, even when every item failed. The HTTP status is about the batch request,
    /// which was understood and processed; what happened to each order is in its own outcome.
    /// A 4xx here would tell a partner to retry the whole batch, including the orders that
    /// succeeded.
    /// </remarks>
    private static async Task<IResult> CreateBulkAsync(
        HttpContext context,
        IPartnerOrderService service,
        IPartnerOrderQueries queries,
        ISalesSettingsGateway settings,
        CancellationToken ct)
    {
        if (!PartnerPrincipal.TryFrom(context.User, out var principal))
            return EndpointResult.Unauthorized();

        var read = await PartnerResults.ReadAsync<CreateBulkOrdersRequest>(context, ct);
        if (read.IsFailure) return EndpointResult.Fail(read.Error!);

        var items = read.Value!.Items ?? [];
        if (items.Count == 0)
            return EndpointResult.Fail(SalesErrors.ValidationFailed("items must not be empty."));

        // From the key's own claim, never the request: a partner must not raise its own batch
        // limit by asking.
        var limits = await settings.GetCommerceLimitsAsync(ct);
        var maxItems = CallerClaims.BulkMaxItemsOf(context.User, limits.BulkDefaultMax);

        if (items.Count > maxItems)
        {
            return EndpointResult.Fail(SalesErrors.ValidationFailed(
                $"A batch may hold at most {maxItems.ToString(CultureInfo.InvariantCulture)} items."));
        }

        var correlationId = CallerClaims.CorrelationIdOf(context);
        var accepted = new List<BulkItem>(items.Count);
        var rejected = new List<BulkOutcomeResponse>();

        for (var index = 0; index < items.Count; index++)
        {
            var validated = PartnerContracts.Validate(items[index]);

            if (validated.IsFailure)
            {
                rejected.Add(new BulkOutcomeResponse(
                    index,
                    items[index].ExternalOrderId ?? string.Empty,
                    "failed",
                    null,
                    new BulkErrorResponse(
                        validated.Error!.Code, validated.Error.Message, validated.Error.Retryable),
                    []));

                continue;
            }

            var (externalOrderId, externalCustomerId, amountMinor, currency, contact) = validated.Value;

            accepted.Add(new BulkItem(index, new CreatePartnerOrderCommand(
                externalOrderId, externalCustomerId, amountMinor, currency, contact)));
        }

        var outcomes = await service.CreateBulkAsync(principal!, accepted, correlationId, ct);

        // One query for every order the batch produced, rather than one per item.
        var createdIds = outcomes
            .Where(outcome => outcome.OrderId is not null)
            .Select(outcome => outcome.OrderId!.Value)
            .ToList();

        var views = (await queries.GetManyAsync(principal!, createdIds, ct))
            .ToDictionary(order => order.Id);

        var body = rejected
            .Concat(outcomes.Select(outcome => new BulkOutcomeResponse(
                outcome.Index,
                outcome.ExternalOrderId,
                outcome.Status.ToString().ToLowerInvariant(),
                outcome.OrderId is { } id && views.TryGetValue(id, out var view)
                    ? PartnerContracts.ToResponse(view, correlationId, outcome.Warnings)
                    : null,
                outcome.Error is null
                    ? null
                    : new BulkErrorResponse(
                        outcome.Error.Code, outcome.Error.Message, outcome.Error.Retryable),
                outcome.Warnings)))
            .OrderBy(outcome => outcome.Index)
            .ToList();

        return PartnerResults.Ok(new BulkOrdersResponse(body));
    }

    /// <summary>
    /// One order, by our id or by the partner's own — <c>ext:{externalOrderId}</c>.
    /// </summary>
    /// <remarks>
    /// Both forms exist because a partner's own id is the one it has to hand after a timeout,
    /// when it does not yet know ours.
    /// </remarks>
    private static async Task<IResult> GetAsync(
        string id, HttpContext context, IPartnerOrderQueries queries, CancellationToken ct)
    {
        if (!PartnerPrincipal.TryFrom(context.User, out var principal))
            return EndpointResult.Unauthorized();

        var order = id.StartsWith(ExternalPrefix, StringComparison.Ordinal)
            ? await queries.GetByExternalIdAsync(principal!, id[ExternalPrefix.Length..], ct)
            : long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var orderId)
                ? await queries.GetAsync(principal!, orderId, ct)
                : Result.Failure<PartnerOrderDto>(new Error("NOT_FOUND", "No such order.", 404));

        return order.IsFailure
            ? EndpointResult.Fail(order.Error!)
            : PartnerResults.Ok(PartnerContracts.ToResponse(
                order.Value!, CallerClaims.CorrelationIdOf(context), []));
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IPartnerOrderQueries queries,
        CancellationToken ct,
        string? externalOrderId = null,
        string? externalCustomerId = null,
        long? customerId = null,
        string? currency = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        long? cursor = null,
        int limit = 50)
    {
        if (!PartnerPrincipal.TryFrom(context.User, out var principal))
            return EndpointResult.Unauthorized();

        var page = await queries.ListAsync(
            principal!,
            new PartnerOrderListQuery(
                externalOrderId, externalCustomerId, customerId, currency, from, to, cursor, limit),
            ct);

        return page.IsFailure ? EndpointResult.Fail(page.Error!) : PartnerResults.Ok(page.Value!);
    }

    private static async Task<IResult> ListForCustomerAsync(
        string id,
        HttpContext context,
        IPartnerOrderQueries queries,
        CancellationToken ct,
        long? cursor = null,
        int limit = 50)
    {
        if (!PartnerPrincipal.TryFrom(context.User, out var principal))
            return EndpointResult.Unauthorized();

        CustomerKey key = id.StartsWith(ExternalPrefix, StringComparison.Ordinal)
            ? new CustomerKey.ByExternalId(id[ExternalPrefix.Length..])
            : long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var customerId)
                ? new CustomerKey.ById(customerId)
                : new CustomerKey.ByExternalId(id);

        var page = await queries.ListForCustomerAsync(principal!, key, cursor, limit, ct);

        return page.IsFailure ? EndpointResult.Fail(page.Error!) : PartnerResults.Ok(page.Value!);
    }
}
