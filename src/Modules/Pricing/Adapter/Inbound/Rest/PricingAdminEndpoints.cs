using System.Globalization;
using System.Security.Claims;

using AuthorizationService.Abstractions.Extensions;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Pricing.QueryApplication;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Money;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Pricing.Adapter.Inbound.Rest;

/// <remarks>Amounts are two-decimal strings; see <see cref="MoneyText"/> for why.</remarks>
public sealed record SetVariantPriceRequest(string Amount, string Currency);

public sealed record SetCurrencyOverrideRequest(string Amount);

/// <remarks>
/// The rate is a string too, and for the same reason: a JSON number is a double to most
/// clients, and a ten-decimal rate does not survive one.
/// </remarks>
public sealed record SetFxOverrideRequest(
    string Base, string Quote, string Rate, DateTimeOffset? ValidUntil);

public sealed record SetCurrencyEnabledRequest(bool Enabled);

public sealed class PricingAdminEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/admin/pricing";

    /// <summary>
    /// The rate column is <c>numeric(18,10)</c>, so anything finer is precision the database
    /// would silently drop. Refusing makes the caller notice.
    /// </summary>
    private const int MaxRateDecimals = 10;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Staff: prices, which are reversible and visible on the product they belong to.
        var staff = app.MapGroup(BasePath)
            .WithTags("Pricing administration")
            .RequireAuthorization(AuthPolicies.AdminStaff)
            .RequirePermission(AdminPermissions.CatalogManage);

        staff.MapGet("/variants/{variantId:long}", async (
            long variantId, IPricingAdminQueries queries, CancellationToken ct) =>
            (await queries.GetVariantPriceAsync(variantId, ct)).ToHttpResult());

        staff.MapPut("/variants/{variantId:long}/price", (
            long variantId, SetVariantPriceRequest request, ClaimsPrincipal user,
            IPricingAdminService service, CancellationToken ct) =>
        {
            if (!MoneyText.TryParseMinor(request.Amount, out var minor))
                return Task.FromResult(Result.Failure(BadAmount(request.Amount)).ToHttpResult());

            return Act(user, actor =>
                service.SetVariantPriceAsync(variantId, minor, request.Currency, actor, ct));
        });

        staff.MapPut("/variants/{variantId:long}/overrides/{currency}", (
            long variantId, string currency, SetCurrencyOverrideRequest request,
            ClaimsPrincipal user, IPricingAdminService service, CancellationToken ct) =>
        {
            if (!MoneyText.TryParseMinor(request.Amount, out var minor))
                return Task.FromResult(Result.Failure(BadAmount(request.Amount)).ToHttpResult());

            return Act(user, actor =>
                service.SetCurrencyOverrideAsync(variantId, currency, minor, actor, ct));
        });

        staff.MapDelete("/variants/{variantId:long}/overrides/{currency}", (
            long variantId, string currency, ClaimsPrincipal user,
            IPricingAdminService service, CancellationToken ct) =>
            Act(user, actor => service.RemoveCurrencyOverrideAsync(variantId, currency, actor, ct)));

        staff.MapGet("/fx/rates", async (
            string? quote, int? page, int? pageSize,
            IPricingAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.ListFxRatesAsync(quote, page ?? 1, pageSize ?? 50, ct))
                .ToHttpResult());

        staff.MapGet("/fx/overrides", async (
            bool? activeOnly, IPricingAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.ListFxOverridesAsync(activeOnly ?? false, ct)).ToHttpResult());

        staff.MapGet("/currencies", async (IPricingAdminQueries queries, CancellationToken ct) =>
            Result.Success(await queries.ListCurrenciesAsync(ct)).ToHttpResult());

        // Super admin: rates and which currencies the shop sells in. Both move every price at
        // once, and neither is visible from looking at a product.
        var super = app.MapGroup(BasePath)
            .WithTags("Pricing administration")
            .RequireAuthorization(AuthPolicies.AdminSuper)
            .RequirePermission(AdminPermissions.FxManage);

        super.MapPost("/fx/overrides", async (
            SetFxOverrideRequest request, ClaimsPrincipal user,
            IPricingAdminService service, CancellationToken ct) =>
        {
            if (!AdminActor.TryFrom(user, out var actor))
                return EndpointResult.Forbidden();

            if (!TryRate(request.Rate, out var rate))
                return Result.Failure(BadRate(request.Rate)).ToHttpResult();

            var result = await service.SetFxOverrideAsync(
                request.Base, request.Quote, rate, request.ValidUntil, actor!, ct);

            return result.ToCreatedHttpResult(
                id => $"{BasePath}/fx/overrides/{request.Base}/{request.Quote}#{id.ToString(CultureInfo.InvariantCulture)}");
        });

        super.MapDelete("/fx/overrides/{base}/{quote}", (
            string @base, string quote, ClaimsPrincipal user,
            IPricingAdminService service, CancellationToken ct) =>
            Act(user, actor => service.ClearFxOverrideAsync(@base, quote, actor, ct)));

        super.MapPut("/currencies/{code}", (
            string code, SetCurrencyEnabledRequest request, ClaimsPrincipal user,
            IPricingAdminService service, CancellationToken ct) =>
            Act(user, actor => service.SetCurrencyEnabledAsync(code, request.Enabled, actor, ct)));

        super.MapPost("/fx/refresh", (
            ClaimsPrincipal user, IPricingAdminService service, CancellationToken ct) =>
            Act(user, actor => service.RequestFxRefreshAsync(actor, ct)));
    }

    /// <summary>
    /// Exactly a decimal with at most ten places, parsed invariantly. No exponent, no sign: a
    /// negative or scientific rate is a client bug, and accepting it would store a number the
    /// resolver would then have to defend against.
    /// </summary>
    private static bool TryRate(string? text, out decimal rate)
    {
        rate = 0;

        if (string.IsNullOrWhiteSpace(text)
            || !decimal.TryParse(
                text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0)
        {
            return false;
        }

        if (decimal.Round(parsed, MaxRateDecimals) != parsed) return false;

        rate = parsed;
        return true;
    }

    private static Error BadAmount(string? text) => new(
        "VALIDATION_FAILED",
        $"'{text}' is not an amount. Use exactly two decimal places, for example \"100.00\".");

    private static Error BadRate(string? text) => new(
        "VALIDATION_FAILED",
        $"'{text}' is not a rate. Use a positive decimal with at most {MaxRateDecimals.ToString(CultureInfo.InvariantCulture)} places.");

    private static async Task<IResult> Act(ClaimsPrincipal user, Func<AdminActor, Task<Result>> action)
    {
        if (!AdminActor.TryFrom(user, out var actor))
            return EndpointResult.Forbidden();

        return (await action(actor!)).ToHttpResult();
    }
}
