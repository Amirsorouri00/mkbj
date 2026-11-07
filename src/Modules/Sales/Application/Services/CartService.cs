using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Carts;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Sales.Application.Services;

/// <summary>
/// The website cart, re-quoted live on every operation.
/// </summary>
/// <remarks>
/// <para>
/// Every path is the same shape: identify the customer, load the cart, ask Catalog what is still
/// sellable and Pricing what it costs <b>now</b>, mutate, save once. The cart never reads a
/// storefront view — those are stale by construction (ADR-0020) and this is a purchasing path.
/// </para>
/// <para>
/// Reading is a write, which looks odd until you ask what the alternative is. If a read did not
/// persist the refreshed prices, the customer would be shown today's total while the cart still
/// held yesterday's, and checkout — which compares the two — would refuse the very thing they
/// had just been shown.
/// </para>
/// </remarks>
internal sealed class CartService(
    ICartRepository carts,
    ISalesCatalogGateway catalog,
    ISalesPricingGateway pricing,
    ISalesCustomerGateway customers,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : ICartService
{
    private const string DefaultCurrency = "USD";

    public Task<Result<CartView>> GetAsync(ShopCaller caller, CancellationToken ct = default) =>
        MutateAsync(caller, static (_, _, _) => { }, ct);

    public Task<Result<CartView>> AddAsync(
        ShopCaller caller, long variantId, int quantity, CancellationToken ct = default) =>
        MutateAsync(
            caller,
            (cart, live, now) =>
            {
                // Checked here as well as on the aggregate: an unsellable or unpriced variant
                // must not enter the cart at all, and the aggregate cannot know either thing.
                if (!live.Prices.TryGetValue(variantId, out var price))
                {
                    throw new DomainException(
                        "NOT_FOUND", "That product is not available to buy right now.");
                }

                cart.AddOrIncrement(ids.Generate(), variantId, quantity, price, now);
            },
            ct,
            alsoQuote: variantId);

    public Task<Result<CartView>> UpdateLineAsync(
        ShopCaller caller, long variantId, int quantity, CancellationToken ct = default) =>
        MutateAsync(
            caller,
            (cart, live, now) =>
            {
                if (!live.Prices.TryGetValue(variantId, out var price))
                {
                    throw new DomainException("NOT_FOUND", "That product is not in your cart.");
                }

                cart.SetQuantity(variantId, quantity, price, now);
            },
            ct);

    public Task<Result<CartView>> RemoveLineAsync(
        ShopCaller caller, long variantId, CancellationToken ct = default) =>
        MutateAsync(caller, (cart, _, now) => cart.Remove(variantId, now), ct);

    public Task<Result<CartView>> SetCurrencyAsync(
        ShopCaller caller, string currency, CancellationToken ct = default) =>
        MutateAsync(caller, static (_, _, _) => { }, ct, currency: currency);

    /// <summary>
    /// The one path every operation takes.
    /// </summary>
    /// <remarks>
    /// Written once rather than five times because the re-quote, the change report and the
    /// single save are the part that must not vary between operations: the one that forgot to
    /// re-quote would be the one that sells at yesterday's price.
    /// </remarks>
    private async Task<Result<CartView>> MutateAsync(
        ShopCaller caller,
        Action<Cart, LivePrices, DateTimeOffset> mutate,
        CancellationToken ct,
        long? alsoQuote = null,
        string? currency = null)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var customer = await customers.EnsureWebsiteCustomerAsync(
            caller.IdentityUserId, caller.Email, caller.EmailVerified, ct);

        if (customer.IsFailure) return Result.Failure<CartView>(customer.Error!);
        if (!customer.Value.IsActive) return Result.Failure<CartView>(SalesErrors.CustomerSuspended);

        // Read once: every price on this cart must be quoted at the same instant.
        var now = clock.UtcNow;

        var cart = await carts.GetByCustomerAsync(customer.Value.CustomerId, ct);
        if (cart is null)
        {
            cart = Cart.Create(ids.Generate(), customer.Value.CustomerId, currency ?? DefaultCurrency, now);
            await carts.AddAsync(cart, ct);
        }

        var target = currency ?? cart.Currency;

        var wanted = cart.Lines.Select(line => line.VariantId).ToHashSet();
        if (alsoQuote is { } extra) wanted.Add(extra);

        var quoted = await QuoteAsync(wanted, target, now, ct);
        if (quoted.IsFailure) return Result.Failure<CartView>(quoted.Error!);

        var shown = cart.Lines.ToDictionary(
            line => line.VariantId, line => line.DisplayedUnitPriceMinor);

        try
        {
            mutate(cart, quoted.Value, now);
        }
        catch (DomainException exception)
        {
            return Result.Failure<CartView>(FromDomain(exception));
        }

        cart.Reprice(target, quoted.Value.Prices, now);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(View(cart, shown, quoted.Value));
    }

    /// <summary>
    /// What each wanted variant is and costs now, omitting whatever is no longer buyable.
    /// </summary>
    /// <remarks>
    /// Sellability and price are different questions answered by different modules, and a line
    /// has to pass both: published does not imply priced, and a price on a withdrawn product is
    /// not an offer.
    /// </remarks>
    private async Task<Result<LivePrices>> QuoteAsync(
        IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now,
        CancellationToken ct)
    {
        if (variantIds.Count == 0) return Result.Success(LivePrices.Empty);

        var sellable = await catalog.GetSellableAsync(variantIds, ct);
        if (sellable.Count == 0) return Result.Success(LivePrices.Empty);

        var quotes = await pricing.QuoteAsync(
            [.. sellable.Select(variant => variant.VariantId)], currency, now, ct);

        if (quotes.IsFailure) return Result.Failure<LivePrices>(quotes.Error!);

        return Result.Success(new LivePrices(
            sellable.ToDictionary(variant => variant.VariantId),
            quotes.Value.ToDictionary(quote => quote.VariantId, quote => quote.ReferenceMinor)));
    }

    private static CartView View(
        Cart cart, IReadOnlyDictionary<long, long> shown, LivePrices live)
    {
        var changes = new List<CartChange>();

        foreach (var (variantId, was) in shown)
        {
            if (!live.Prices.TryGetValue(variantId, out var isNow))
            {
                changes.Add(new CartChange(variantId, CartChange.Removed, was, null));
            }
            else if (isNow != was)
            {
                changes.Add(new CartChange(variantId, CartChange.Repriced, was, isNow));
            }
        }

        var lines = cart.Lines
            .Select(line => new CartLineView(
                line.VariantId,
                live.Variants.TryGetValue(line.VariantId, out var variant) ? variant.Sku : string.Empty,
                variant?.ProductTitle ?? string.Empty,
                line.Quantity,
                line.DisplayedUnitPriceMinor,
                line.LineTotalMinor))
            .ToList();

        return new CartView(cart.Currency, lines, cart.TotalMinor, changes);
    }

    private static Error FromDomain(DomainException exception) => exception.Code switch
    {
        "NOT_FOUND" => new Error("NOT_FOUND", exception.Message, 404),
        _ => SalesErrors.ValidationFailed(exception.Message),
    };

    private sealed record LivePrices(
        IReadOnlyDictionary<long, SalesVariant> Variants,
        IReadOnlyDictionary<long, long> Prices)
    {
        public static readonly LivePrices Empty = new(
            new Dictionary<long, SalesVariant>(), new Dictionary<long, long>());
    }
}
