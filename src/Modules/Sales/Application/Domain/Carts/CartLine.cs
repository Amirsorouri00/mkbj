using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Sales.Application.Domain.Carts;

/// <summary>
/// One variant in a cart, at the price the customer was last shown.
/// </summary>
/// <remarks>
/// <c>PriceQuotedAt</c> is kept because the displayed price is evidence, not agreement: when
/// checkout refuses a changed price, this is what says how old the figure the customer was
/// looking at actually was.
/// </remarks>
public sealed class CartLine : Entity<long>
{
    private CartLine() { }

    internal CartLine(
        long id, long variantId, int quantity, long displayedUnitPriceMinor, DateTimeOffset now)
    {
        Id = id;
        VariantId = variantId;
        Quantity = quantity;
        DisplayedUnitPriceMinor = displayedUnitPriceMinor;
        PriceQuotedAt = now;
    }

    public long VariantId { get; private set; }
    public int Quantity { get; private set; }
    public long DisplayedUnitPriceMinor { get; private set; }
    public DateTimeOffset PriceQuotedAt { get; private set; }

    public long LineTotalMinor => checked(Quantity * DisplayedUnitPriceMinor);

    internal void Set(int quantity, long displayedUnitPriceMinor, DateTimeOffset now)
    {
        Quantity = quantity;
        DisplayedUnitPriceMinor = displayedUnitPriceMinor;
        PriceQuotedAt = now;
    }
}
