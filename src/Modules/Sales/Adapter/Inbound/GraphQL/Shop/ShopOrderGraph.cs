using System.Security.Claims;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Sales.Application;
using EwaysVNext.Modules.Sales.Application.Money;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using EwaysVNext.Modules.Sales.QueryApplication.Shop;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.GraphQL.Shop;

public sealed record CheckoutInput(MoneyGraph ExpectedTotal);

/// <remarks>
/// One of <c>order</c> and <c>priceChanged</c> is set, and <c>code</c> says which. A union would
/// be tidier GraphQL, but every client of this payload has to handle both cases anyway, and a
/// nullable pair is the shape they can read without a fragment.
/// </remarks>
public sealed record CheckoutPayload(
    ShopOrderDto? Order, CartGraph? PriceChanged, string? Code);

/// <summary>A website customer's own orders and documents.</summary>
[ExtendObjectType(typeof(Query))]
public sealed class ShopOrderQueriesGraph
{
    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<CursorPage<ShopOrderDto>> MyOrdersAsync(
        ClaimsPrincipal user,
        [Service] IShopOrderQueries queries,
        [Service] IShopCustomerResolver customers,
        long? cursor = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        var customerId = await CustomerIdAsync(user, customers, ct);

        return await queries.ListMyOrdersAsync(customerId, cursor, limit, ct);
    }

    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<ShopOrderDto> OrderAsync(
        long id,
        ClaimsPrincipal user,
        [Service] IShopOrderQueries queries,
        [Service] IShopCustomerResolver customers,
        CancellationToken ct = default)
    {
        var customerId = await CustomerIdAsync(user, customers, ct);

        return (await queries.GetMyOrderAsync(customerId, id, ct)).ValueOrThrow();
    }

    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<string> DocumentPdfUrlAsync(
        long documentId,
        ClaimsPrincipal user,
        [Service] IInvoiceService invoices,
        [Service] IShopCustomerResolver customers,
        CancellationToken ct = default)
    {
        var customerId = await CustomerIdAsync(user, customers, ct);

        var url = await invoices.GetPdfUrlAsync(
            new DocumentAccess.WebsiteCustomer(customerId), documentId, ct);

        return url.ValueOrThrow().ToString();
    }

    internal static async Task<long> CustomerIdAsync(
        ClaimsPrincipal user, IShopCustomerResolver customers, CancellationToken ct) =>
        (await customers.ResolveAsync(ShopGraphCaller.Of(user), ct)).ValueOrThrow();
}

[ExtendObjectType(typeof(Mutation))]
public sealed class ShopCheckoutMutations
{
    /// <remarks>
    /// A changed price comes back as a payload rather than an error, because it is not one: the
    /// customer asked to buy at a total that is no longer available, and the next thing they
    /// need is the fresh cart.
    /// </remarks>
    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<CheckoutPayload> CheckoutAsync(
        CheckoutInput input,
        ClaimsPrincipal user,
        [Service] ICheckoutService checkout,
        [Service] IShopOrderQueries queries,
        [Service] IShopCustomerResolver customers,
        [Service] ICorrelationContext correlation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var caller = ShopGraphCaller.Of(user);

        if (!AmountText.TryParseMinor(input.ExpectedTotal.Amount, out var expectedMinor))
        {
            // Thrown rather than returned as a payload: a malformed amount is the client's bug,
            // not a shopping outcome the customer should be shown a cart for.
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("expectedTotal.amount must be a decimal amount with two places.")
                    .SetCode("VALIDATION_FAILED")
                    .Build());
        }

        var placed = (await checkout.CheckoutAsync(
            caller, expectedMinor, input.ExpectedTotal.Currency,
            correlation.CorrelationId, ct)).ValueOrThrow();

        if (placed is CheckoutOutcome.PriceChanged changed)
            return new CheckoutPayload(null, changed.FreshCart.ToGraph(), SalesErrors.PriceChanged.Code);

        var orderId = ((CheckoutOutcome.Placed)placed).OrderId;
        var customerId = await ShopOrderQueriesGraph.CustomerIdAsync(user, customers, ct);

        return new CheckoutPayload(
            (await queries.GetMyOrderAsync(customerId, orderId, ct)).ValueOrThrow(), null, null);
    }
}
