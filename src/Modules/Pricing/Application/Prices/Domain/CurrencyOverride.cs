using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Pricing.Application.Prices.Domain;

/// <summary>
/// A price an admin set directly in one currency, instead of letting it be converted.
/// </summary>
/// <remarks>
/// This exists because conversion produces honest but unsellable numbers: €19.99 becomes
/// $21.61, and a seller wants $21.99. An override says "in this currency, this is the price",
/// and nothing recomputes it when the rate moves.
/// </remarks>
public sealed class CurrencyOverride : Entity<long>
{
    /// <summary>
    /// The one price list every customer sees. The column exists so a second list — a partner
    /// tier, a campaign — can be added without a migration that touches every row.
    /// </summary>
    public const string DefaultPriceList = "default";

    public long VariantPriceId { get; private set; }

    public string Currency { get; private set; } = null!;

    public long AmountMinor { get; private set; }

    public string PriceList { get; private set; } = DefaultPriceList;

    public DateTimeOffset UpdatedAt { get; private set; }

    private CurrencyOverride() { }

    internal CurrencyOverride(long id, long variantPriceId, string currency, long amountMinor,
        string priceList, DateTimeOffset now)
    {
        Id = id;
        VariantPriceId = variantPriceId;
        Currency = currency;
        AmountMinor = amountMinor;
        PriceList = priceList;
        UpdatedAt = now;
    }

    internal void SetAmount(long amountMinor, DateTimeOffset now)
    {
        if (AmountMinor == amountMinor) return;

        AmountMinor = amountMinor;
        UpdatedAt = now;
    }

    internal static void EnsurePositiveAmount(long amountMinor)
    {
        if (amountMinor <= 0)
        {
            throw new DomainException(
                "PRICE_AMOUNT_INVALID", "A price must be greater than zero.");
        }
    }
}
