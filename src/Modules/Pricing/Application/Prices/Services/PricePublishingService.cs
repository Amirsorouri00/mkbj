using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Pricing.Application.Prices.Services;

public sealed class PricePublishingService(
    IVariantPriceRepository prices,
    ICurrencyRepository currencies,
    ICatalogVariantGateway variants,
    IPricingChangeNotifier notifier,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : IPricePublishingPort
{
    public async Task<Result> SetReferencePriceAsync(
        long variantId, long amountMinor, string currency, PriceProvenance provenance,
        CancellationToken ct = default)
    {
        // Enabled or not: a price may be collected in a currency the seller has stopped selling
        // in, and refusing to record it would lose the observation rather than the decision.
        if (await currencies.GetByCodeAsync(currency, ct) is null)
            return Result.Failure(PricingErrors.CurrencyNotFound);

        if (!await variants.VariantExistsAsync(variantId, ct))
            return Result.Failure(PricingErrors.VariantNotFound);

        var existing = await prices.GetByVariantIdAsync(variantId, ct);

        try
        {
            if (existing is null)
            {
                await prices.AddAsync(
                    VariantPrice.Create(
                        ids.Generate(), variantId, amountMinor, currency, provenance, clock.UtcNow),
                    ct);
            }
            else
            {
                // The same observation arriving again writes the same values, so a re-run of an
                // import leaves the row exactly as it was.
                existing.SetBase(amountMinor, currency, provenance, clock.UtcNow);
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error(exception.Code, exception.Message));
        }

        await unitOfWork.SaveChangesAsync(ct);
        await notifier.PricesChangedAsync(ct);
        return Result.Success();
    }
}
