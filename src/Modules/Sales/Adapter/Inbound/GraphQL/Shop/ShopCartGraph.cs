using System.Security.Claims;
using EwaysVNext.Core.Application.Security;
using EwaysVNext.Modules.Sales.Application.Money;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.GraphQL.Shop;

public sealed record MoneyGraph(string Amount, string Currency);

public sealed record CartLineGraph(
    long VariantId,
    string Sku,
    string ProductTitle,
    int Quantity,
    MoneyGraph UnitPrice,
    MoneyGraph LineTotal);

public sealed record CartChangeGraph(
    long VariantId, string Kind, string? OldUnitPrice, string? NewUnitPrice);

public sealed record CartGraph(
    string Currency,
    IReadOnlyList<CartLineGraph> Lines,
    MoneyGraph Total,
    IReadOnlyList<CartChangeGraph> Changes);

public sealed record AddToCartInput(long VariantId, int Quantity);

public sealed record UpdateCartLineInput(long VariantId, int Quantity);

public sealed record RemoveCartLineInput(long VariantId);

public sealed record SetCartCurrencyInput(string Currency);

/// <summary>
/// The cart over GraphQL — the same service the REST cart calls, for a client that would rather
/// fetch its cart alongside the page it is drawing.
/// </summary>
/// <remarks>
/// <c>[Authorize]</c> sits on each resolver, never on the class. A type-level attribute on an
/// <c>[ExtendObjectType]</c> is merged onto the whole <c>Query</c> and would gate every other
/// module's fields; <c>GraphQLGateScopeTests</c> fails the build on that.
/// </remarks>
[ExtendObjectType(typeof(Query))]
public sealed class ShopCartQueries
{
    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<CartGraph> CartAsync(
        ClaimsPrincipal user,
        [Service] ICartService carts,
        CancellationToken ct = default) =>
        (await carts.GetAsync(ShopGraphCaller.Of(user), ct)).ValueOrThrow().ToGraph();
}

[ExtendObjectType(typeof(Mutation))]
public sealed class ShopCartMutations
{
    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<CartGraph> AddToCartAsync(
        AddToCartInput input,
        ClaimsPrincipal user,
        [Service] ICartService carts,
        CancellationToken ct = default) =>
        (await carts.AddAsync(ShopGraphCaller.Of(user), input.VariantId, input.Quantity, ct))
            .ValueOrThrow().ToGraph();

    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<CartGraph> UpdateCartLineAsync(
        UpdateCartLineInput input,
        ClaimsPrincipal user,
        [Service] ICartService carts,
        CancellationToken ct = default) =>
        (await carts.UpdateLineAsync(ShopGraphCaller.Of(user), input.VariantId, input.Quantity, ct))
            .ValueOrThrow().ToGraph();

    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<CartGraph> RemoveCartLineAsync(
        RemoveCartLineInput input,
        ClaimsPrincipal user,
        [Service] ICartService carts,
        CancellationToken ct = default) =>
        (await carts.RemoveLineAsync(ShopGraphCaller.Of(user), input.VariantId, ct))
            .ValueOrThrow().ToGraph();

    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<CartGraph> SetCartCurrencyAsync(
        SetCartCurrencyInput input,
        ClaimsPrincipal user,
        [Service] ICartService carts,
        CancellationToken ct = default) =>
        (await carts.SetCurrencyAsync(ShopGraphCaller.Of(user), input.Currency, ct))
            .ValueOrThrow().ToGraph();
}

internal static class CartGraphMapping
{
    public static CartGraph ToGraph(this CartView cart) => new(
        cart.Currency,
        [.. cart.Lines.Select(line => new CartLineGraph(
            line.VariantId,
            line.Sku,
            line.ProductTitle,
            line.Quantity,
            new MoneyGraph(AmountText.Format(line.UnitPriceMinor), cart.Currency),
            new MoneyGraph(AmountText.Format(line.LineTotalMinor), cart.Currency)))],
        new MoneyGraph(AmountText.Format(cart.TotalMinor), cart.Currency),
        [.. cart.Changes.Select(change => new CartChangeGraph(
            change.VariantId,
            change.Kind,
            change.OldUnitPriceMinor is { } old ? AmountText.Format(old) : null,
            change.NewUnitPriceMinor is { } now ? AmountText.Format(now) : null))]);
}

/// <summary>
/// The caller behind a shop resolver.
/// </summary>
/// <remarks>
/// A token that satisfied the policy but carries no usable shop identity is forbidden, not a
/// server error: the request was authenticated, it just does not say who is shopping.
/// </remarks>
internal static class ShopGraphCaller
{
    public static ShopCaller Of(ClaimsPrincipal user) =>
        ShopIdentityClaims.Read(user) is { } identity
            ? new ShopCaller(identity.IdentityUserId, identity.Email, identity.EmailVerified)
            : throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This token does not identify a shop customer.")
                    .SetCode("FORBIDDEN")
                    .Build());
}
