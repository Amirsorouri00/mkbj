using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;

namespace EwaysVNext.Modules.Pricing.Application.Prices.Domain;

/// <summary>
/// What one variant costs: a single base price, plus the currencies an admin has priced by hand.
/// </summary>
/// <remarks>
/// One aggregate per variant, and the base price is the only one that is ever converted. Keeping
/// a per-currency price for everything would mean every rate change rewrote the whole catalogue;
/// keeping only a base means a quote is computed when it is asked for, which is also when a
/// customer can be told the rate it used.
/// </remarks>
public sealed class VariantPrice : AggregateRoot<long>
{
    private readonly List<CurrencyOverride> _overrides = [];

    public long VariantId { get; private set; }

    public long BaseAmountMinor { get; private set; }

    public string BaseCurrency { get; private set; } = null!;

    public PriceProvenance Provenance { get; private set; } = PriceProvenance.Manual;

    public IReadOnlyCollection<CurrencyOverride> Overrides => _overrides.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private VariantPrice() { }

    public static VariantPrice Create(long id, long variantId, long baseAmountMinor,
        string baseCurrency, PriceProvenance provenance, DateTimeOffset now)
    {
        if (id <= 0)
        {
            throw new DomainException("PRICE_ID_INVALID", "Price ID must be positive.");
        }

        if (variantId <= 0)
        {
            throw new DomainException("PRICE_VARIANT_ID_INVALID", "Variant ID must be positive.");
        }

        CurrencyOverride.EnsurePositiveAmount(baseAmountMinor);

        return new VariantPrice
        {
            Id = id,
            VariantId = variantId,
            BaseAmountMinor = baseAmountMinor,
            BaseCurrency = Currency.NormalizeCode(baseCurrency),
            Provenance = provenance ?? PriceProvenance.Manual,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the base price and the story behind it together. They are never set apart: a new
    /// price with the old provenance would cite an observation that recorded something else.
    /// </summary>
    public void SetBase(long baseAmountMinor, string baseCurrency, PriceProvenance provenance,
        DateTimeOffset now)
    {
        CurrencyOverride.EnsurePositiveAmount(baseAmountMinor);
        var normalizedCurrency = Currency.NormalizeCode(baseCurrency);

        if (BaseAmountMinor == baseAmountMinor
            && string.Equals(BaseCurrency, normalizedCurrency, StringComparison.Ordinal)
            && Provenance == (provenance ?? PriceProvenance.Manual))
        {
            return;
        }

        BaseAmountMinor = baseAmountMinor;
        BaseCurrency = normalizedCurrency;
        Provenance = provenance ?? PriceProvenance.Manual;
        UpdatedAt = now;
    }

    /// <summary>
    /// Sets the price in one currency, replacing any existing one for that currency and list.
    /// <para>
    /// An upsert rather than add-or-fail: an admin changing a price does not think of it as
    /// removing one and adding another, and the unique index per (currency, list) means there is
    /// only ever one row to change anyway.
    /// </para>
    /// </summary>
    public void SetOverride(long overrideId, string currency, long amountMinor, DateTimeOffset now,
        string priceList = CurrencyOverride.DefaultPriceList)
    {
        if (overrideId <= 0)
        {
            throw new DomainException("PRICE_OVERRIDE_ID_INVALID", "Override ID must be positive.");
        }

        CurrencyOverride.EnsurePositiveAmount(amountMinor);
        var normalizedCurrency = Currency.NormalizeCode(currency);
        var normalizedList = NormalizePriceList(priceList);

        if (Find(normalizedCurrency, normalizedList) is { } existing)
        {
            existing.SetAmount(amountMinor, now);
        }
        else
        {
            _overrides.Add(new CurrencyOverride(
                overrideId, Id, normalizedCurrency, amountMinor, normalizedList, now));
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// Removes a currency's override, so that currency goes back to being converted. Returns
    /// whether there was one — a caller clearing an override it is unsure about should not have
    /// to catch an exception to find out.
    /// </summary>
    public bool RemoveOverride(string currency, DateTimeOffset now,
        string priceList = CurrencyOverride.DefaultPriceList)
    {
        var existing = Find(Currency.NormalizeCode(currency), NormalizePriceList(priceList));
        if (existing is null) return false;

        _overrides.Remove(existing);
        UpdatedAt = now;
        return true;
    }

    /// <summary>The override for a currency and list, or null — what the resolver asks.</summary>
    public CurrencyOverride? FindOverride(string currency,
        string priceList = CurrencyOverride.DefaultPriceList)
    {
        if (string.IsNullOrWhiteSpace(currency)) return null;

        return Find(currency.Trim().ToUpperInvariant(), NormalizePriceList(priceList));
    }

    private CurrencyOverride? Find(string currency, string priceList) =>
        _overrides.FirstOrDefault(candidate =>
            string.Equals(candidate.Currency, currency, StringComparison.Ordinal)
            && string.Equals(candidate.PriceList, priceList, StringComparison.Ordinal));

    private static string NormalizePriceList(string? priceList) =>
        string.IsNullOrWhiteSpace(priceList) ? CurrencyOverride.DefaultPriceList : priceList.Trim();
}
