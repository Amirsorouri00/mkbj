using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Ports.Inbound;

/// <summary>
/// What happened when the customer pressed the button.
/// </summary>
/// <remarks>
/// A price change is not an error. The customer asked to buy a basket at a total they were
/// shown, and that total is no longer available — so they are given the fresh cart and asked
/// again, rather than charged an amount they never agreed to or told something failed.
/// </remarks>
public abstract record CheckoutOutcome
{
    private CheckoutOutcome() { }

    public sealed record Placed(long OrderId) : CheckoutOutcome;

    public sealed record PriceChanged(CartView FreshCart) : CheckoutOutcome;
}

public interface ICheckoutService
{
    /// <summary>
    /// Turns the caller's cart into an order, if it still costs what they were told.
    /// </summary>
    /// <remarks>
    /// <paramref name="expectedTotalMinor"/> is the only money a shop request may carry, and it
    /// is a claim about what was displayed rather than an instruction about what to charge. It
    /// can only cause a refusal, never a price.
    /// </remarks>
    Task<Result<CheckoutOutcome>> CheckoutAsync(
        ShopCaller caller,
        long expectedTotalMinor,
        string expectedCurrency,
        string correlationId,
        CancellationToken ct = default);
}
