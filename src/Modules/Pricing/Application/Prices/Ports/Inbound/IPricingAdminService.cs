using EwaysVNext.Core.Application;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;

/// <summary>
/// Everything an admin does to prices and rates.
/// <para>
/// Split by who may do it. Staff set prices, which are reversible and visible on the product they
/// belong to. Only a super admin touches exchange rates and which currencies the shop sells in —
/// both change every price at once, and neither is obvious from looking at a product.
/// </para>
/// </summary>
public interface IPricingAdminService
{
    Task<Result> SetVariantPriceAsync(long variantId, long baseAmountMinor, string baseCurrency,
        AdminActor actor, CancellationToken ct = default);

    Task<Result> SetCurrencyOverrideAsync(long variantId, string currency, long amountMinor,
        AdminActor actor, CancellationToken ct = default);

    Task<Result> RemoveCurrencyOverrideAsync(long variantId, string currency, AdminActor actor,
        CancellationToken ct = default);

    Task<Result<long>> SetFxOverrideAsync(string @base, string quote, decimal rate,
        DateTimeOffset? validUntil, AdminActor actor, CancellationToken ct = default);

    Task<Result> ClearFxOverrideAsync(string @base, string quote, AdminActor actor,
        CancellationToken ct = default);

    Task<Result> SetCurrencyEnabledAsync(string code, bool enabled, AdminActor actor,
        CancellationToken ct = default);

    Task<Result> RequestFxRefreshAsync(AdminActor actor, CancellationToken ct = default);
}
