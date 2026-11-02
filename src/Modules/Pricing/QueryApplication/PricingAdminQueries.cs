using EwaysVNext.Database;
using EwaysVNext.Modules.Pricing.Application;
using EwaysVNext.Outcomes;
using EwaysVNext.Queries.Application.Paginations;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Pricing.QueryApplication;

public sealed class PricingAdminQueries(EwaysVNextDbContext db) : IPricingAdminQueries
{
    private const int MaxPageSize = 100;

    public async Task<Result<VariantPriceAdminDto>> GetVariantPriceAsync(
        long variantId, CancellationToken ct = default)
    {
        var price = await db.VariantPrices.AsNoTracking()
            .Include(candidate => candidate.Overrides)
            .FirstOrDefaultAsync(candidate => candidate.VariantId == variantId, ct);

        if (price is null) return Result.Failure<VariantPriceAdminDto>(PricingErrors.PriceMissing);

        return Result.Success(new VariantPriceAdminDto(
            price.VariantId,
            price.BaseAmountMinor,
            price.BaseCurrency,
            price.Overrides
                .OrderBy(@override => @override.Currency, StringComparer.Ordinal)
                .Select(@override => new CurrencyOverrideDto(
                    @override.Currency, @override.AmountMinor, @override.PriceList))
                .ToList(),
            price.Provenance.ObservationId,
            price.Provenance.SourceUrl,
            price.Provenance.CollectedAt,
            price.UpdatedAt));
    }

    public async Task<PaginatedQuery<FxRateDto>> ListFxRatesAsync(
        string? quote, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var rates = db.FxRates.AsNoTracking();

        var filter = quote?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(filter)) rates = rates.Where(rate => rate.Quote == filter);

        var totalCount = await rates.CountAsync(ct);

        // Newest first: an admin opening this screen is asking what the latest rate is, and
        // scrolling back through history is the rarer question.
        var items = await rates
            .OrderByDescending(rate => rate.FetchedAt)
            .ThenBy(rate => rate.Quote)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(rate => new FxRateDto(
                rate.Id, rate.Base, rate.Quote, rate.Rate, rate.Provider, rate.RateDate, rate.FetchedAt))
            .ToListAsync(ct);

        return new PaginatedQuery<FxRateDto>(
            page, pageSize, (int)Math.Ceiling(totalCount / (double)pageSize), totalCount, items);
    }

    public async Task<IReadOnlyList<FxOverrideDto>> ListFxOverridesAsync(
        bool activeOnly, CancellationToken ct = default)
    {
        var overrides = db.FxRateOverrides.AsNoTracking();
        if (activeOnly) overrides = overrides.Where(@override => @override.IsActive);

        return await overrides
            .OrderBy(@override => @override.Base).ThenBy(@override => @override.Quote)
            .Select(@override => new FxOverrideDto(
                @override.Id, @override.Base, @override.Quote, @override.Rate,
                @override.SetBy, @override.SetAt, @override.ValidUntil, @override.IsActive))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CurrencyAdminDto>> ListCurrenciesAsync(CancellationToken ct = default) =>
        await db.Currencies.AsNoTracking()
            .OrderBy(currency => currency.Code)
            .Select(currency => new CurrencyAdminDto(
                currency.Id, currency.Code, currency.MinorUnits, currency.IsEnabled))
            .ToListAsync(ct);
}
