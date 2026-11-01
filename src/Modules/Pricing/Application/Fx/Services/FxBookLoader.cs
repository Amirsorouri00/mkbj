using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;
using EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Services;

/// <summary>
/// Reads the rate book and the currency list once per call.
/// <para>
/// Shared by quoting and by USD conversion so both see the same book. If they each loaded their
/// own, an order could be priced against one set of rates and checked against the seller's limits
/// using another — and the two would disagree precisely when a refresh landed between them.
/// </para>
/// </summary>
public sealed class FxBookLoader(
    ICurrencyRepository currencies,
    IFxRateRepository rates,
    IFxRateOverrideRepository overrides,
    IPricingPolicyGateway policy)
{
    public async Task<(FxRateBook Book, IReadOnlyList<Currency> Currencies)> LoadAsync(
        CancellationToken ct = default)
    {
        var all = await currencies.GetAllAsync(ct);

        var book = new FxRateBook(
            await rates.GetLatestPerPairAsync(ct),
            await overrides.GetActiveAsync(ct),
            await policy.GetFxMaxRateAgeAsync(ct));

        return (book, all);
    }

    /// <summary>
    /// Checks a currency is one the seller sells in, normalising it on the way.
    /// <para>
    /// Unknown and disabled are the same answer on purpose: both mean "we do not sell in this",
    /// and telling a caller which would say more about the seller's configuration than they
    /// asked for.
    /// </para>
    /// </summary>
    public static Result<string> RequireEnabled(IReadOnlyList<Currency> currencies, string code)
    {
        var normalized = Currency.NormalizeCode(code);

        return currencies.Any(currency =>
            currency.IsEnabled && string.Equals(currency.Code, normalized, StringComparison.Ordinal))
            ? Result.Success(normalized)
            : Result.Failure<string>(PricingErrors.CurrencyNotEnabled);
    }
}
