using System.Globalization;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound.Gateways;

/// <summary>
/// Catalog, as Sales sees it.
/// </summary>
/// <remarks>
/// The whole point of these gateways is that the provider's types stop here. Catalog may rename
/// a field tomorrow; the cost of that should be one mapping, not every invoice in the system.
/// </remarks>
internal sealed class SalesCatalogGateway(ISellableVariantPort sellable) : ISalesCatalogGateway
{
    public async Task<IReadOnlyList<SalesVariant>> GetSellableAsync(
        IReadOnlyCollection<long>? variantIds, CancellationToken ct = default) =>
        [.. (await sellable.GetSellableAsync(variantIds, ct)).Select(variant => new SalesVariant(
            variant.VariantId,
            variant.ProductId,
            variant.Sku,
            variant.Title,
            variant.BrandId,
            variant.BrandName,
            variant.PrimaryCategoryId,
            variant.CategoryPath,
            variant.Attributes))];

    public Task<long> GetVersionAsync(CancellationToken ct = default) =>
        sellable.GetVersionAsync(ct);
}

internal sealed class SalesPricingGateway(
    IPriceQuotePort quotes, IFxSnapshotPort fx) : ISalesPricingGateway
{
    public async Task<Result<IReadOnlyList<SalesPriceQuote>>> QuoteAsync(
        IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now,
        CancellationToken ct = default)
    {
        var quoted = await quotes.QuoteAsync(variantIds, currency, now, ct);
        if (quoted.IsFailure) return Result.Failure<IReadOnlyList<SalesPriceQuote>>(quoted.Error!);

        return Result.Success<IReadOnlyList<SalesPriceQuote>>(
            [.. quoted.Value!.Values.Select(Map)]);
    }

    public async Task<Result<SalesUsdConversion>> ToUsdAsync(
        long amountMinor, string currency, DateTimeOffset now, CancellationToken ct = default)
    {
        var converted = await fx.ToUsdAsync(amountMinor, currency, now, ct);

        return converted.IsFailure
            ? Result.Failure<SalesUsdConversion>(converted.Error!)
            : Result.Success(new SalesUsdConversion(converted.Value!.AmountMinor));
    }

    public Task<long> GetVersionAsync(CancellationToken ct = default) => quotes.GetVersionAsync(ct);

    /// <remarks>
    /// The band travels with the quote rather than being recomputed here, so the band a price was
    /// issued against and the band an order is checked against are the same arithmetic on the
    /// same number.
    /// </remarks>
    private static SalesPriceQuote Map(PriceQuote quote) => new(
        quote.VariantId,
        quote.ReferenceMinor,
        quote.LoMinor,
        quote.HiMinor,
        new PriceSnapshot(
            quote.ReferenceMinor,
            MapSource(quote.Source),
            quote.Fx?.Rate,
            quote.Fx?.FxRateId,
            quote.Fx?.FxOverrideId,
            quote.Fx?.RateAt,
            quote.Fx?.Provenance,
            BaseAmountMinor: quote.ReferenceMinor,
            BaseCurrency: quote.Fx?.Base ?? quote.Currency));

    private static LinePriceSource MapSource(PriceSource source) => source switch
    {
        PriceSource.Base => LinePriceSource.Base,
        PriceSource.Override => LinePriceSource.Override,
        PriceSource.Fx => LinePriceSource.Fx,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown price source."),
    };
}

internal sealed class SalesCustomerGateway(
    ICustomerResolutionPort resolution, IWebsiteCustomerPort website) : ISalesCustomerGateway
{
    public async Task<SalesPartnerCustomer?> FindPartnerCustomerAsync(
        long partnerId, string externalCustomerId, CancellationToken ct = default) =>
        Map(await resolution.FindPartnerCustomerAsync(partnerId, externalCustomerId, ct));

    public async Task<Result<SalesPartnerCustomer>> CreatePartnerCustomerAsync(
        long partnerId, string externalCustomerId, PartnerContact contact, string correlationId,
        CancellationToken ct = default)
    {
        var created = await resolution.CreatePartnerCustomerAsync(
            partnerId, externalCustomerId,
            new ContactInput(contact.Email, contact.Phone, contact.FullName),
            correlationId, ct);

        return created.IsFailure
            ? Result.Failure<SalesPartnerCustomer>(created.Error!)
            : Result.Success(Map(created.Value)!);
    }

    public Task<Result> SyncPartnerContactAsync(
        long customerId, long partnerId, PartnerContact contact, string correlationId,
        CancellationToken ct = default) =>
        resolution.SyncPartnerContactAsync(
            customerId, partnerId,
            new ContactInput(contact.Email, contact.Phone, contact.FullName),
            correlationId, ct);

    public async Task<Result<SalesInvoiceParty>> GetInvoicePartyAsync(
        long customerId, CancellationToken ct = default)
    {
        var party = await resolution.GetInvoicePartyAsync(customerId, ct);

        return party.IsFailure
            ? Result.Failure<SalesInvoiceParty>(party.Error!)
            : Result.Success(new SalesInvoiceParty(
                party.Value!.CustomerId,
                party.Value.FullName,
                party.Value.CompanyName,
                party.Value.Email,
                party.Value.Phone,
                Flatten(party.Value.BillingAddress),
                party.Value.RegistrationNumber));
    }

    public async Task<Result<SalesWebsiteCustomer>> EnsureWebsiteCustomerAsync(
        Guid identityUserId, string email, bool emailVerified, CancellationToken ct = default)
    {
        var ensured = await website.EnsureFromIdentityAsync(identityUserId, email, emailVerified, ct);

        return ensured.IsFailure
            ? Result.Failure<SalesWebsiteCustomer>(ensured.Error!)
            : Result.Success(new SalesWebsiteCustomer(
                ensured.Value!.CustomerId, ensured.Value.EmailVerified, ensured.Value.IsActive));
    }

    private static SalesPartnerCustomer? Map(PartnerCustomerRef? reference) =>
        reference is null
            ? null
            : new SalesPartnerCustomer(
                reference.CustomerId, reference.Email, reference.Phone, reference.FullName);

    /// <summary>
    /// The address as one line, which is how an invoice prints it.
    /// </summary>
    /// <remarks>
    /// Flattened here rather than in the renderer: the order stores the buyer block as it was,
    /// and a structured address stored today would have to be formatted by whatever renderer
    /// exists in three years.
    /// </remarks>
    private static string? Flatten(AddressSnapshot? address)
    {
        if (address is null) return null;

        var parts = new List<string>(4) { address.Line1 };
        if (!string.IsNullOrWhiteSpace(address.Line2)) parts.Add(address.Line2);

        parts.Add(string.Create(
            CultureInfo.InvariantCulture, $"{address.City} {address.PostalCode}").Trim());
        parts.Add(address.CountryIso2);

        return string.Join(", ", parts);
    }
}

internal sealed class SalesSettingsGateway(ISettingsPort settings) : ISalesSettingsGateway
{
    public async Task<(SellerBlock Seller, IReadOnlyList<string> RequiredBuyerFields, bool GstRegistered)>
        GetSellerAsync(CancellationToken ct = default)
    {
        var seller = await settings.GetSellerAsync(ct);

        return (
            new SellerBlock(
                seller.LegalName, seller.Uen, seller.Address, seller.Email, seller.InvoiceFooter),
            seller.RequiredBuyerFields,
            seller.GstRegistered);
    }

    public async Task<PaymentInstructionBlock> GetActivePaymentInstructionAsync(
        CancellationToken ct = default)
    {
        var instruction = await settings.GetActivePaymentInstructionAsync(ct);

        return new PaymentInstructionBlock(instruction.Id, instruction.Version, instruction.Text);
    }

    public async Task<SalesCommerceLimits> GetCommerceLimitsAsync(CancellationToken ct = default)
    {
        var limits = await settings.GetCommerceLimitsAsync(ct);

        return new SalesCommerceLimits(
            limits.MinUsdMinor,
            limits.MaxUsdMinor,
            limits.BulkDefaultMax,
            limits.CompositionDeadlineMs,
            limits.CompositionMaxLines);
    }
}
