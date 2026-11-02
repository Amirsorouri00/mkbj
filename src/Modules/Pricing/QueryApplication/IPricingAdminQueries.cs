using EwaysVNext.Outcomes;
using EwaysVNext.Queries.Application.Paginations;

namespace EwaysVNext.Modules.Pricing.QueryApplication;

/// <summary>
/// The administrative read side. Unlike the shopper's, it shows disabled currencies and
/// withdrawn overrides — those screens are where someone goes to find exactly those.
/// </summary>
public interface IPricingAdminQueries
{
    Task<Result<VariantPriceAdminDto>> GetVariantPriceAsync(
        long variantId, CancellationToken ct = default);

    Task<PaginatedQuery<FxRateDto>> ListFxRatesAsync(
        string? quote, int page, int pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<FxOverrideDto>> ListFxOverridesAsync(
        bool activeOnly, CancellationToken ct = default);

    Task<IReadOnlyList<CurrencyAdminDto>> ListCurrenciesAsync(CancellationToken ct = default);
}
