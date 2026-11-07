using System.Security.Claims;
using EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Common;
using EwaysVNext.Modules.Sales.Application;
using EwaysVNext.Modules.Sales.Application.Money;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.QueryApplication.Shop;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Shop;

public sealed record CheckoutRequest(MoneyRequest? ExpectedTotal);

/// <remarks>
/// The amount is a string in the strict minor-unit form, never a JSON number: a number would
/// lose a cent to binary floating point somewhere between the browser and the comparison, and
/// this value exists to be compared exactly.
/// </remarks>
public sealed record MoneyRequest(string? Amount, string? Currency);

public sealed record PdfUrlResponse(string Url);

/// <summary>
/// Checkout, order history and document links for a website customer.
/// </summary>
/// <remarks>
/// <c>expectedTotal</c> is the only money any shop request carries, and it is a claim about what
/// was displayed rather than an instruction about what to charge — it can only cause a refusal.
/// <c>SalesArchitectureTests</c> fails the build if another money-shaped field appears here.
/// </remarks>
public sealed class ShopOrderEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var shop = app.MapGroup("/api/v1/shop")
            .WithTags("Shop Orders")
            .RequireAuthorization(AuthPolicies.ShopCustomer);

        shop.MapPost("/checkout", CheckoutAsync);
        shop.MapGet("/orders", ListAsync);
        shop.MapGet("/orders/{id:long}", GetAsync);
        shop.MapGet("/documents/{id:long}/pdf-url", PdfUrlAsync);
    }

    private static async Task<IResult> CheckoutAsync(
        CheckoutRequest request,
        HttpContext context,
        ClaimsPrincipal user,
        ICheckoutService checkout,
        IShopOrderQueries queries,
        IShopCustomerResolver customers,
        CancellationToken ct)
    {
        var caller = CallerClaims.ShopCallerOf(user);
        if (caller is null) return EndpointResult.Fail(Error.Forbidden);

        if (!AmountText.TryParseMinor(request.ExpectedTotal?.Amount, out var expectedMinor))
        {
            return EndpointResult.Fail(SalesErrors.ValidationFailed(
                "expectedTotal.amount must be a decimal amount with two places, such as \"12.00\"."));
        }

        var placed = await checkout.CheckoutAsync(
            caller,
            expectedMinor,
            request.ExpectedTotal?.Currency ?? string.Empty,
            CallerClaims.CorrelationIdOf(context),
            ct);

        if (placed.IsFailure) return EndpointResult.Fail(placed.Error!);

        // A changed price is 409 with the fresh cart attached, not an error body the customer
        // has to interpret: the next thing they will do is look at what it now costs.
        if (placed.Value is CheckoutOutcome.PriceChanged changed)
        {
            return EndpointResult.Fail(
                SalesErrors.PriceChanged, details: new { cart = changed.FreshCart.ToResponse() });
        }

        var orderId = ((CheckoutOutcome.Placed)placed.Value!).OrderId;

        var customerId = await customers.ResolveAsync(caller, ct);
        if (customerId.IsFailure) return EndpointResult.Fail(customerId.Error!);

        var order = await queries.GetMyOrderAsync(customerId.Value, orderId, ct);

        return order.ToCreatedHttpResult(_ => $"/api/v1/shop/orders/{orderId}");
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        IShopOrderQueries queries,
        IShopCustomerResolver customers,
        CancellationToken ct,
        long? cursor = null,
        int limit = 20)
    {
        var customerId = await ResolveAsync(user, customers, ct);
        if (customerId.IsFailure) return EndpointResult.Fail(customerId.Error!);

        var page = await queries.ListMyOrdersAsync(customerId.Value, cursor, limit, ct);

        return Result.Success(page).ToHttpResult();
    }

    private static async Task<IResult> GetAsync(
        long id,
        ClaimsPrincipal user,
        IShopOrderQueries queries,
        IShopCustomerResolver customers,
        CancellationToken ct)
    {
        var customerId = await ResolveAsync(user, customers, ct);
        if (customerId.IsFailure) return EndpointResult.Fail(customerId.Error!);

        return (await queries.GetMyOrderAsync(customerId.Value, id, ct)).ToHttpResult();
    }

    /// <remarks>
    /// The access is <see cref="DocumentAccess.WebsiteCustomer"/>, so the entitlement is part of
    /// the query rather than a comparison afterwards — a load-then-check shape would have read
    /// another customer's invoice into this process before deciding not to return it.
    /// </remarks>
    private static async Task<IResult> PdfUrlAsync(
        long id,
        ClaimsPrincipal user,
        IInvoiceService invoices,
        IShopCustomerResolver customers,
        CancellationToken ct)
    {
        var customerId = await ResolveAsync(user, customers, ct);
        if (customerId.IsFailure) return EndpointResult.Fail(customerId.Error!);

        var url = await invoices.GetPdfUrlAsync(
            new DocumentAccess.WebsiteCustomer(customerId.Value), id, ct);

        return url.ToHttpResult(uri => new PdfUrlResponse(uri.ToString()));
    }

    private static Task<Result<long>> ResolveAsync(
        ClaimsPrincipal user, IShopCustomerResolver customers, CancellationToken ct)
    {
        var caller = CallerClaims.ShopCallerOf(user);

        return caller is null
            ? Task.FromResult(Result.Failure<long>(Error.Forbidden))
            : customers.ResolveAsync(caller, ct);
    }
}
