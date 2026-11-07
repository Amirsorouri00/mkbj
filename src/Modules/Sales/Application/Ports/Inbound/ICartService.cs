using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Ports.Inbound;

/// <summary>The shop customer behind a request, as the token presents them.</summary>
public sealed record ShopCaller(Guid IdentityUserId, string Email, bool EmailVerified);

public sealed record CartLineView(
    long VariantId,
    string Sku,
    string ProductTitle,
    int Quantity,
    long UnitPriceMinor,
    long LineTotalMinor);

/// <summary>
/// Something the system did to the cart that the customer did not ask for.
/// </summary>
/// <remarks>
/// Returned rather than applied silently. A line that changed price or disappeared between two
/// visits is the customer's business, and a cart that quietly costs more than it did is how
/// someone pays an amount they never agreed to.
/// </remarks>
public sealed record CartChange(
    long VariantId, string Kind, long? OldUnitPriceMinor, long? NewUnitPriceMinor)
{
    public const string Repriced = "repriced";
    public const string Removed = "removed";
}

public sealed record CartView(
    string Currency,
    IReadOnlyList<CartLineView> Lines,
    long TotalMinor,
    IReadOnlyList<CartChange> Changes);

/// <summary>
/// The website cart.
/// </summary>
/// <remarks>
/// Every operation re-quotes against live tables before answering, and never reads a storefront
/// materialized view (ADR-0020): the views are stale by construction, and a cart is a purchasing
/// path. Reading the cart is therefore itself a write — the refreshed displayed prices are
/// persisted, so that the customer's next checkout compares against what they were actually
/// shown.
/// </remarks>
public interface ICartService
{
    Task<Result<CartView>> GetAsync(ShopCaller caller, CancellationToken ct = default);

    Task<Result<CartView>> AddAsync(
        ShopCaller caller, long variantId, int quantity, CancellationToken ct = default);

    Task<Result<CartView>> UpdateLineAsync(
        ShopCaller caller, long variantId, int quantity, CancellationToken ct = default);

    Task<Result<CartView>> RemoveLineAsync(
        ShopCaller caller, long variantId, CancellationToken ct = default);

    Task<Result<CartView>> SetCurrencyAsync(
        ShopCaller caller, string currency, CancellationToken ct = default);
}
