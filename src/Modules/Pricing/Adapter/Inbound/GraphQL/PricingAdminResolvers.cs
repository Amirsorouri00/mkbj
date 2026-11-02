using System.Globalization;
using System.Security.Claims;

using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Pricing.QueryApplication;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Money;
using EwaysVNext.Presentation.Endpoint.Security;
using EwaysVNext.Queries.Application.Paginations;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Pricing.Adapter.Inbound.GraphQL;

/// <remarks>
/// Amounts and rates are strings here too. GraphQL has no decimal scalar, so a Float would make
/// rounding the client's problem on every request — the one place that must not happen is money.
/// </remarks>
public sealed record SetVariantPriceInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long VariantId,
    string Amount,
    string Currency);

public sealed record SetCurrencyOverrideInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long VariantId,
    string Currency,
    string Amount);

public sealed record RemoveCurrencyOverrideInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long VariantId,
    string Currency);

public sealed record SetFxOverrideInput(
    string Base, string Quote, string Rate, DateTimeOffset? ValidUntil);

public sealed record ClearFxOverrideInput(string Base, string Quote);

public sealed record SetCurrencyEnabledInput(string Code, bool Enabled);

// Both attributes on every resolver, never on the class: a type-level attribute on an
// [ExtendObjectType] is merged onto the whole Query/Mutation and would gate every other module's
// fields with pricing's policy.

[ExtendObjectType(typeof(Query))]
public sealed class PricingAdminGraphQLQueries
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<VariantPriceAdminDto> AdminVariantPriceAsync(
        [GraphQLType(typeof(NonNullType<IdType>))] long variantId,
        [Service] IPricingAdminQueries queries, CancellationToken ct = default) =>
        ResultGraphQLExtensions.ValueOrThrow(await queries.GetVariantPriceAsync(variantId, ct));

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<PaginatedQuery<FxRateDto>> AdminFxRatesAsync(
        string? quote, int page, int pageSize,
        [Service] IPricingAdminQueries queries, CancellationToken ct = default) =>
        queries.ListFxRatesAsync(quote, page, pageSize, ct);

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<IReadOnlyList<FxOverrideDto>> AdminFxOverridesAsync(
        bool activeOnly, [Service] IPricingAdminQueries queries, CancellationToken ct = default) =>
        queries.ListFxOverridesAsync(activeOnly, ct);

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<IReadOnlyList<CurrencyAdminDto>> AdminCurrenciesAsync(
        [Service] IPricingAdminQueries queries, CancellationToken ct = default) =>
        queries.ListCurrenciesAsync(ct);
}

[ExtendObjectType(typeof(Mutation))]
public sealed class PricingAdminGraphQLMutations
{
    private const int MaxRateDecimals = 10;

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<bool> SetVariantPriceAsync(
        SetVariantPriceInput input, ClaimsPrincipal user,
        [Service] IPricingAdminService service, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Act(service.SetVariantPriceAsync(
            input.VariantId, Minor(input.Amount), input.Currency, Actor(user), ct));
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<bool> SetCurrencyOverrideAsync(
        SetCurrencyOverrideInput input, ClaimsPrincipal user,
        [Service] IPricingAdminService service, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Act(service.SetCurrencyOverrideAsync(
            input.VariantId, input.Currency, Minor(input.Amount), Actor(user), ct));
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<bool> RemoveCurrencyOverrideAsync(
        RemoveCurrencyOverrideInput input, ClaimsPrincipal user,
        [Service] IPricingAdminService service, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Act(service.RemoveCurrencyOverrideAsync(
            input.VariantId, input.Currency, Actor(user), ct));
    }

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.FxManage)]
    [GraphQLType(typeof(NonNullType<IdType>))]
    public async Task<long> SetFxOverrideAsync(
        SetFxOverrideInput input, ClaimsPrincipal user,
        [Service] IPricingAdminService service, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ResultGraphQLExtensions.ValueOrThrow(await service.SetFxOverrideAsync(
            input.Base, input.Quote, Rate(input.Rate), input.ValidUntil, Actor(user), ct));
    }

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.FxManage)]
    public Task<bool> ClearFxOverrideAsync(
        ClearFxOverrideInput input, ClaimsPrincipal user,
        [Service] IPricingAdminService service, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Act(service.ClearFxOverrideAsync(input.Base, input.Quote, Actor(user), ct));
    }

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.FxManage)]
    public Task<bool> SetCurrencyEnabledAsync(
        SetCurrencyEnabledInput input, ClaimsPrincipal user,
        [Service] IPricingAdminService service, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Act(service.SetCurrencyEnabledAsync(input.Code, input.Enabled, Actor(user), ct));
    }

    [Authorize(Policy = AuthPolicies.AdminSuper)]
    [RequirePermission(AdminPermissions.FxManage)]
    public Task<bool> RequestFxRefreshAsync(
        ClaimsPrincipal user, [Service] IPricingAdminService service, CancellationToken ct = default) =>
        Act(service.RequestFxRefreshAsync(Actor(user), ct));

    private static long Minor(string? amount)
    {
        if (!MoneyText.TryParseMinor(amount, out var minor))
        {
            throw new EwaysVNext.Outcomes.Error("VALIDATION_FAILED",
                    $"'{amount}' is not an amount. Use exactly two decimal places, for example \"100.00\".")
                .ToGraphQLException();
        }

        return minor;
    }

    private static decimal Rate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !decimal.TryParse(
                text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0
            || decimal.Round(parsed, MaxRateDecimals) != parsed)
        {
            throw new EwaysVNext.Outcomes.Error("VALIDATION_FAILED",
                    $"'{text}' is not a rate. Use a positive decimal with at most 10 places.")
                .ToGraphQLException();
        }

        return parsed;
    }

    private static AdminActor Actor(ClaimsPrincipal user) =>
        AdminActor.TryFrom(user, out var actor)
            ? actor!
            : throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("The token passed the admin policy but carries no usable subject.")
                .SetCode("UNAUTHORIZED")
                .Build());

    private static async Task<bool> Act(Task<Result> action)
    {
        (await action).ThrowIfFailure();
        return true;
    }
}
