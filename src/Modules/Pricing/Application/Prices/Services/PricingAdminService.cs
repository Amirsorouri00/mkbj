using System.Globalization;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Jobs;
using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;
using EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Pricing.Application.Prices.Services;

public sealed class PricingAdminService(
    IVariantPriceRepository prices,
    ICurrencyRepository currencies,
    IFxRateOverrideRepository fxOverrides,
    ICatalogVariantGateway variants,
    IPricingChangeNotifier notifier,
    IJobQueue jobs,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : IPricingAdminService
{
    private const string PriceTarget = "VariantPrice";
    private const string FxTarget = "FxRateOverride";
    private const string CurrencyTarget = "Currency";

    /// <summary>
    /// One key for the whole refresh. A second request while one is queued is dropped: refreshing
    /// twice in a row fetches the same published rates and writes nothing new.
    /// </summary>
    private const string FxRefreshDedupeKey = "fx-refresh";

    public async Task<Result> SetVariantPriceAsync(long variantId, long baseAmountMinor,
        string baseCurrency, AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Enabled or not: an admin may price in a currency the shop does not currently sell in,
        // which is how a currency is made ready before it is switched on.
        if (await currencies.GetByCodeAsync(baseCurrency, ct) is null)
            return Result.Failure(PricingErrors.CurrencyNotFound);

        if (!await variants.VariantExistsAsync(variantId, ct))
            return Result.Failure(PricingErrors.VariantNotFound);

        var price = await prices.GetByVariantIdAsync(variantId, ct);
        var before = price is null ? null : Snapshot(price);

        try
        {
            if (price is null)
            {
                price = VariantPrice.Create(
                    ids.Generate(), variantId, baseAmountMinor, baseCurrency,
                    PriceProvenance.Manual, clock.UtcNow);
                await prices.AddAsync(price, ct);
            }
            else
            {
                // Manual provenance replaces whatever the import recorded: an admin typing a
                // price is the new reason it is what it is.
                price.SetBase(baseAmountMinor, baseCurrency, PriceProvenance.Manual, clock.UtcNow);
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure(Domain(exception));
        }

        audit.Record(actor.ToAudit(), "price.set", PriceTarget, Key(variantId), before, Snapshot(price));
        await unitOfWork.SaveChangesAsync(ct);

        await notifier.PricesChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetCurrencyOverrideAsync(long variantId, string currency,
        long amountMinor, AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Enabled, unlike a base price: an override is a price customers will be shown, so
        // setting one in a currency nobody can buy in is a mistake rather than preparation.
        var enabled = await EnabledCurrencyAsync(currency, ct);
        if (enabled.IsFailure) return Result.Failure(enabled.Error!);

        var price = await prices.GetByVariantIdAsync(variantId, ct);
        if (price is null) return Result.Failure(PricingErrors.PriceMissing);

        var before = Snapshot(price);

        try
        {
            price.SetOverride(ids.Generate(), enabled.Value!, amountMinor, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(Domain(exception));
        }

        audit.Record(actor.ToAudit(), "price.override.set", PriceTarget, Key(variantId),
            before, Snapshot(price));
        await unitOfWork.SaveChangesAsync(ct);

        await notifier.PricesChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveCurrencyOverrideAsync(long variantId, string currency,
        AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var price = await prices.GetByVariantIdAsync(variantId, ct);
        if (price is null) return Result.Failure(PricingErrors.PriceMissing);

        var before = Snapshot(price);
        if (!price.RemoveOverride(currency, clock.UtcNow)) return Result.Success();

        audit.Record(actor.ToAudit(), "price.override.removed", PriceTarget, Key(variantId),
            before, Snapshot(price));
        await unitOfWork.SaveChangesAsync(ct);

        await notifier.PricesChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result<long>> SetFxOverrideAsync(string @base, string quote, decimal rate,
        DateTimeOffset? validUntil, AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Checked before any IO. The admin:super policy already gates the endpoint; this is the
        // second lock, because an editable permission row could unlock the first.
        if (!actor.IsSuperAdmin) return Result.Failure<long>(Error.Forbidden);

        var existing = await fxOverrides.GetForPairAsync(@base, quote, ct);
        var before = existing is null ? null : Snapshot(existing);
        var id = existing?.Id ?? ids.Generate();

        try
        {
            if (existing is null)
            {
                existing = FxRateOverride.Create(id, @base, quote, rate, actor.UserId, clock.UtcNow, validUntil);
                await fxOverrides.AddAsync(existing, ct);
            }
            else
            {
                // In place: the pair's row is unique whether active or not, so replacing is the
                // only way to set it again, and it leaves no window with no override.
                existing.Replace(rate, actor.UserId, clock.UtcNow, validUntil);
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure<long>(Domain(exception));
        }

        audit.Record(actor.ToAudit(), "fx.override.set", FxTarget, $"{existing.Base}/{existing.Quote}",
            before, Snapshot(existing));

        // A concurrent first insert collides on the pair; a stale replace loses on xmin. Both
        // mean another admin set this pair first, which is a conflict rather than a crash.
        var saved = await unitOfWork.TrySaveChangesAsync(ct);
        if (saved != SaveOutcome.Saved) return Result.Failure<long>(PricingErrors.FxOverrideConflict);

        await notifier.FxChangedAsync(ct);
        return Result.Success(id);
    }

    public async Task<Result> ClearFxOverrideAsync(string @base, string quote, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsSuperAdmin) return Result.Failure(Error.Forbidden);

        var existing = await fxOverrides.GetForPairAsync(@base, quote, ct);
        if (existing is null || !existing.IsActive) return Result.Success();

        var before = Snapshot(existing);
        existing.Deactivate(clock.UtcNow);

        audit.Record(actor.ToAudit(), "fx.override.cleared", FxTarget, $"{existing.Base}/{existing.Quote}",
            before, Snapshot(existing));

        var saved = await unitOfWork.TrySaveChangesAsync(ct);
        if (saved != SaveOutcome.Saved) return Result.Failure(PricingErrors.FxOverrideConflict);

        await notifier.FxChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetCurrencyEnabledAsync(string code, bool enabled, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsSuperAdmin) return Result.Failure(Error.Forbidden);

        var currency = await currencies.GetByCodeAsync(code, ct);
        if (currency is null) return Result.Failure(PricingErrors.CurrencyNotFound);

        if (currency.IsEnabled == enabled) return Result.Success();

        var before = new { currency.Code, currency.IsEnabled };
        currency.SetEnabled(enabled, clock.UtcNow);

        audit.Record(actor.ToAudit(), "currency.enabled.set", CurrencyTarget, currency.Code,
            before, new { currency.Code, currency.IsEnabled });
        await unitOfWork.SaveChangesAsync(ct);

        // FX, not prices: which currencies the shop sells in changes the whole resolved-rate
        // table, not one product's number.
        await notifier.FxChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RequestFxRefreshAsync(AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsSuperAdmin) return Result.Failure(Error.Forbidden);

        await jobs.EnqueueAsync(
            JobTypes.FxRefresh, new FxRefreshRequest(), runAt: null, dedupeKey: FxRefreshDedupeKey, ct);

        audit.Record(actor.ToAudit(), "fx.refresh.requested", FxTarget, FxRefreshDedupeKey, null, null);
        return Result.Success();
    }

    private async Task<Result<string>> EnabledCurrencyAsync(string code, CancellationToken ct)
    {
        var currency = await currencies.GetByCodeAsync(code, ct);

        return currency is { IsEnabled: true }
            ? Result.Success(currency.Code)
            : Result.Failure<string>(
                currency is null ? PricingErrors.CurrencyNotFound : PricingErrors.CurrencyNotEnabled);
    }

    private static string Key(long variantId) => variantId.ToString(CultureInfo.InvariantCulture);

    private static object Snapshot(VariantPrice price) => new
    {
        price.VariantId,
        price.BaseAmountMinor,
        price.BaseCurrency,
        Overrides = price.Overrides
            .Select(@override => new { @override.Currency, @override.AmountMinor, @override.PriceList })
            .OrderBy(@override => @override.Currency, StringComparer.Ordinal)
            .ToArray(),
    };

    /// <summary>
    /// The override's whole state, before and after. This is the <b>only</b> history an override
    /// has — the row is replaced in place, so the audit trail is where "what was the rate last
    /// week" is answered.
    /// </summary>
    private static object Snapshot(FxRateOverride @override) => new
    {
        @override.Base,
        @override.Quote,
        @override.Rate,
        @override.SetBy,
        @override.SetAt,
        @override.ValidUntil,
        @override.IsActive,
    };

    private static Error Domain(DomainException exception) => new(exception.Code, exception.Message);
}

/// <summary>The FX refresh job carries nothing: it fetches whatever the provider publishes.</summary>
public sealed record FxRefreshRequest();
