using System.Security.Claims;
using EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Common;
using EwaysVNext.Modules.Sales.Application;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Shop;

/// <summary>
/// The website cart, over HTTP.
/// </summary>
/// <remarks>
/// A thin adapter: every rule, every re-quote and every refusal belongs to
/// <see cref="ICartService"/>. Note what the request shapes do <b>not</b> carry — no price, no
/// total. A cart that accepted a price from the browser would let the browser set it.
/// </remarks>
public sealed class ShopCartEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/api/v1/shop/cart")
            .WithTags("Shop Cart")
            .RequireAuthorization(AuthPolicies.ShopCustomer);

        cart.MapGet("/", (ClaimsPrincipal user, ICartService carts, CancellationToken ct) =>
            Run(user, caller => carts.GetAsync(caller, ct)));

        cart.MapPost("/lines", (
            AddCartLineRequest request, ClaimsPrincipal user, ICartService carts,
            CancellationToken ct) =>
            Run(user, caller => carts.AddAsync(caller, request.VariantId, request.Quantity, ct)));

        cart.MapPut("/lines/{variantId:long}", (
            long variantId, UpdateCartLineRequest request, ClaimsPrincipal user,
            ICartService carts, CancellationToken ct) =>
            Run(user, caller => carts.UpdateLineAsync(caller, variantId, request.Quantity, ct)));

        cart.MapDelete("/lines/{variantId:long}", (
            long variantId, ClaimsPrincipal user, ICartService carts, CancellationToken ct) =>
            Run(user, caller => carts.RemoveLineAsync(caller, variantId, ct)));

        cart.MapPut("/currency", (
            SetCartCurrencyRequest request, ClaimsPrincipal user, ICartService carts,
            CancellationToken ct) =>
            Run(user, caller => carts.SetCurrencyAsync(caller, request.Currency ?? string.Empty, ct)));
    }

    /// <summary>
    /// Resolves the caller, runs the operation, and returns the one cart shape.
    /// </summary>
    /// <remarks>
    /// A token that passed the policy but carries no usable shop identity is a 403, not a 500:
    /// the request was authenticated, it just does not say who is shopping.
    /// </remarks>
    private static async Task<IResult> Run(
        ClaimsPrincipal user, Func<ShopCaller, Task<Result<CartView>>> operation)
    {
        var caller = CallerClaims.ShopCallerOf(user);
        if (caller is null) return EndpointResult.Fail(Error.Forbidden);

        return (await operation(caller)).ToHttpResult(cart => cart.ToResponse());
    }
}
