using EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Partner;
using EwaysVNext.Modules.Sales.Application.Money;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Shop;

public sealed record AddCartLineRequest(long VariantId, int Quantity);

public sealed record UpdateCartLineRequest(int Quantity);

public sealed record SetCartCurrencyRequest(string? Currency);

public sealed record CartLineResponse(
    long VariantId,
    string Sku,
    string ProductTitle,
    int Quantity,
    MoneyDto UnitPrice,
    MoneyDto LineTotal);

/// <remarks>
/// <c>Changes</c> is part of the cart, not an error channel. A line that was repriced or dropped
/// while the customer was elsewhere is something they must be told, and a client that ignores
/// the field still shows the right total.
/// </remarks>
public sealed record CartChangeResponse(
    long VariantId, string Kind, string? OldUnitPrice, string? NewUnitPrice);

public sealed record CartResponse(
    string Currency,
    IReadOnlyList<CartLineResponse> Lines,
    MoneyDto Total,
    IReadOnlyList<CartChangeResponse> Changes);

internal static class ShopMapping
{
    public static CartResponse ToResponse(this CartView cart) => new(
        cart.Currency,
        [.. cart.Lines.Select(line => new CartLineResponse(
            line.VariantId,
            line.Sku,
            line.ProductTitle,
            line.Quantity,
            new MoneyDto(AmountText.Format(line.UnitPriceMinor), cart.Currency),
            new MoneyDto(AmountText.Format(line.LineTotalMinor), cart.Currency)))],
        new MoneyDto(AmountText.Format(cart.TotalMinor), cart.Currency),
        [.. cart.Changes.Select(change => new CartChangeResponse(
            change.VariantId,
            change.Kind,
            change.OldUnitPriceMinor is { } old ? AmountText.Format(old) : null,
            change.NewUnitPriceMinor is { } now ? AmountText.Format(now) : null))]);
}
