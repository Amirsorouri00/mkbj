using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Sales.Application.Services;

/// <summary>
/// Turns a cart into a website order, or refuses because the price moved.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is written until the price is agreed.</b> Everything that can refuse — the email
/// check, the empty cart, the live re-quote, the total comparison — happens before any number is
/// allocated or any row added. A checkout that failed after allocating an invoice number would
/// leave a gap in a sequence an auditor reads as a missing invoice.
/// </para>
/// <para>
/// The comparison is per line <b>and</b> in total. Two lines that moved in opposite directions
/// leave the total unchanged while both prices are wrong, and a customer who was shown $20 and
/// $30 should not be charged $25 and $25 because the sum still comes to $50.
/// </para>
/// </remarks>
internal sealed class CheckoutService(
    ICartService carts,
    ICartRepository cartRepository,
    IOrderRepository orders,
    ISalesCatalogGateway catalog,
    ISalesPricingGateway pricing,
    ISalesCustomerGateway customers,
    ISalesSettingsGateway settings,
    IDocumentNumberAllocator numbers,
    ITaxPolicy taxPolicy,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    public async Task<Result<CheckoutOutcome>> CheckoutAsync(
        ShopCaller caller,
        long expectedTotalMinor,
        string expectedCurrency,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        // Cheapest refusal first, and before any quote: an unverified address cannot be invoiced
        // to, so there is no point pricing anything.
        if (!caller.EmailVerified)
            return Result.Failure<CheckoutOutcome>(SalesErrors.EmailNotVerified);

        var customer = await customers.EnsureWebsiteCustomerAsync(
            caller.IdentityUserId, caller.Email, caller.EmailVerified, ct);

        if (customer.IsFailure) return Result.Failure<CheckoutOutcome>(customer.Error!);
        if (!customer.Value!.IsActive)
            return Result.Failure<CheckoutOutcome>(SalesErrors.CustomerSuspended);

        // Read once: the order, its documents and its USD figure must all agree about when.
        var now = clock.UtcNow;
        var customerId = customer.Value.CustomerId;

        var cart = await cartRepository.GetByCustomerAsync(customerId, ct);
        if (cart is null || cart.Lines.Count == 0)
        {
            return Result.Failure<CheckoutOutcome>(
                SalesErrors.ValidationFailed("There is nothing in your cart."));
        }

        var variantIds = cart.Lines.Select(line => line.VariantId).ToHashSet();

        var sellable = await catalog.GetSellableAsync(variantIds, ct);
        var sellableById = sellable.ToDictionary(variant => variant.VariantId);

        var quotes = await pricing.QuoteAsync(
            [.. sellableById.Keys], cart.Currency, now, ct);

        if (quotes.IsFailure) return Result.Failure<CheckoutOutcome>(quotes.Error!);

        var live = quotes.Value!.ToDictionary(quote => quote.VariantId, quote => quote);

        if (HasMoved(cart, live, expectedTotalMinor, expectedCurrency))
            return await PriceChangedAsync(caller, ct);

        var party = await customers.GetInvoicePartyAsync(customerId, ct);
        if (party.IsFailure) return Result.Failure<CheckoutOutcome>(party.Error!);

        var (seller, requiredBuyerFields, _) = await settings.GetSellerAsync(ct);

        var buyer = new BuyerBlock(
            party.Value!.CustomerId,
            ExternalCustomerId: null,
            party.Value.FullName,
            party.Value.CompanyName,
            party.Value.Email ?? caller.Email,
            party.Value.Phone,
            party.Value.BillingAddress,
            party.Value.RegistrationNumber);

        if (BuyerFieldRequirements.Missing(requiredBuyerFields, buyer).Count > 0)
            return Result.Failure<CheckoutOutcome>(SalesErrors.InvoiceBuyerFieldsMissing);

        var total = cart.TotalMinor;

        var usd = await pricing.ToUsdAsync(total, cart.Currency, now, ct);
        if (usd.IsFailure) return Result.Failure<CheckoutOutcome>(usd.Error!);

        var instruction = await settings.GetActivePaymentInstructionAsync(ct);

        var lines = cart.Lines
            .Select(line => new OrderLineInput(
                ids.Generate(),
                Snapshot(sellableById[line.VariantId]),
                line.Quantity,
                live[line.VariantId].ReferenceMinor,
                live[line.VariantId].Snapshot))
            .ToList();

        var orderNumber = await numbers.NextAsync(DocumentNumberKind.Order, ct);
        var proFormaNumber = await numbers.NextAsync(DocumentNumberKind.ProForma, ct);

        Order order;
        try
        {
            order = Order.CheckoutWebsite(
                new WebsiteOrderHeader(
                    ids.Generate(), orderNumber, customerId, cart.Currency,
                    usd.Value!.UsdMinor, correlationId),
                lines,
                new IssuedDocumentRef(ids.Generate(), proFormaNumber),
                seller,
                requiredBuyerFields,
                buyer,
                taxPolicy.Assess(total, cart.Currency),
                instruction,
                now);
        }
        catch (DomainException exception)
        {
            return Result.Failure<CheckoutOutcome>(
                SalesErrors.ValidationFailed(exception.Message));
        }

        await orders.AddAsync(order, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await ClearCartAsync(cart, now, order.Id, ct);

        return Result.Success<CheckoutOutcome>(new CheckoutOutcome.Placed(order.Id));
    }

    /// <summary>
    /// Whether anything the customer was shown is no longer true.
    /// </summary>
    /// <remarks>
    /// A line that cannot be quoted at all counts as moved: it is unsellable or unpriced, and
    /// either way the basket the customer is looking at is not one they can buy.
    /// </remarks>
    private static bool HasMoved(
        Domain.Carts.Cart cart,
        Dictionary<long, SalesPriceQuote> live,
        long expectedTotalMinor,
        string expectedCurrency)
    {
        if (!string.Equals(cart.Currency, expectedCurrency, StringComparison.OrdinalIgnoreCase))
            return true;

        if (cart.TotalMinor != expectedTotalMinor) return true;

        foreach (var line in cart.Lines)
        {
            if (!live.TryGetValue(line.VariantId, out var quote)) return true;
            if (quote.ReferenceMinor != line.DisplayedUnitPriceMinor) return true;
        }

        return false;
    }

    /// <summary>
    /// The refusal, carrying the cart as it now stands.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="ICartService"/>, which re-quotes and persists what it found — so
    /// the customer's next attempt compares against the figures this response just showed them.
    /// This is the one write a refused checkout causes, and it belongs to the cart rather than
    /// to the order.
    /// </remarks>
    private async Task<Result<CheckoutOutcome>> PriceChangedAsync(
        ShopCaller caller, CancellationToken ct)
    {
        var fresh = await carts.GetAsync(caller, ct);

        return fresh.IsFailure
            ? Result.Failure<CheckoutOutcome>(fresh.Error!)
            : Result.Success<CheckoutOutcome>(new CheckoutOutcome.PriceChanged(fresh.Value!));
    }

    /// <summary>
    /// Empties the cart in a save of its own, and survives failing to.
    /// </summary>
    /// <remarks>
    /// The order is already committed. Failing the request now would tell a customer their order
    /// did not happen when it did, and they would place it again — so a stale cart is logged and
    /// lived with. It is visibly wrong and harmless; a duplicate order is neither.
    /// </remarks>
    private async Task ClearCartAsync(
        Domain.Carts.Cart cart, DateTimeOffset now, long orderId, CancellationToken ct)
    {
        try
        {
            cart.Clear(now);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Order {OrderId} was placed but its cart could not be cleared.",
                orderId);
        }
    }

    private static VariantSnapshot Snapshot(SalesVariant variant) => new(
        variant.VariantId,
        variant.ProductId,
        variant.Sku,
        variant.ProductTitle,
        variant.BrandName,
        variant.CategoryPath,
        variant.Attributes);
}
