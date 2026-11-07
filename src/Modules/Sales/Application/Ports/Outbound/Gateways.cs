using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Ports.Outbound;

/// <summary>
/// One sellable variant, as Sales sees it.
/// </summary>
/// <remarks>
/// A local copy of Catalog's <c>SellableVariant</c>, not the type itself. The duplication is the
/// anti-corruption layer doing its job: Catalog may add, rename or reshape fields for its own
/// reasons, and the blast radius of that should be one gateway rather than the order aggregate,
/// the composition engine and every invoice ever printed.
/// </remarks>
public sealed record SalesVariant(
    long VariantId,
    long ProductId,
    string Sku,
    string ProductTitle,
    long BrandId,
    string BrandName,
    long PrimaryCategoryId,
    IReadOnlyList<string> CategoryPath,
    IReadOnlyDictionary<string, string> Attributes);

public interface ISalesCatalogGateway
{
    Task<IReadOnlyList<SalesVariant>> GetSellableAsync(
        IReadOnlyCollection<long>? variantIds, CancellationToken ct = default);

    /// <summary>
    /// What the catalogue looked like, as one number.
    /// </summary>
    /// <remarks>
    /// Cheap enough to ask on every request, which is what lets the candidate cache notice a
    /// change rather than wait out its TTL.
    /// </remarks>
    Task<long> GetVersionAsync(CancellationToken ct = default);
}

/// <summary>One variant's price and the band a composed line may move inside.</summary>
public sealed record SalesPriceQuote(
    long VariantId, long ReferenceMinor, long LoMinor, long HiMinor, PriceSnapshot Snapshot);

public sealed record SalesUsdConversion(long UsdMinor);

public interface ISalesPricingGateway
{
    Task<Result<IReadOnlyList<SalesPriceQuote>>> QuoteAsync(
        IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>
    /// The amount in US dollars, for checking against limits written in dollars.
    /// </summary>
    /// <remarks>Not a price: it is never shown to a buyer and never billed.</remarks>
    Task<Result<SalesUsdConversion>> ToUsdAsync(
        long amountMinor, string currency, DateTimeOffset now, CancellationToken ct = default);

    Task<long> GetVersionAsync(CancellationToken ct = default);
}

public sealed record PartnerContact(string? Email, string? Phone, string? FullName);

public sealed record SalesPartnerCustomer(
    long CustomerId, string? Email, string? Phone, string? FullName);

/// <remarks>The address is flattened to the one line an invoice prints.</remarks>
public sealed record SalesInvoiceParty(
    long CustomerId,
    string? FullName,
    string? CompanyName,
    string? Email,
    string? Phone,
    string? BillingAddress,
    string? RegistrationNumber);

public sealed record SalesWebsiteCustomer(long CustomerId, bool EmailVerified, bool IsActive);

public interface ISalesCustomerGateway
{
    Task<SalesPartnerCustomer?> FindPartnerCustomerAsync(
        long partnerId, string externalCustomerId, CancellationToken ct = default);

    Task<Result<SalesPartnerCustomer>> CreatePartnerCustomerAsync(
        long partnerId, string externalCustomerId, PartnerContact contact, string correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Pushes the partner's latest contact details back to Customer.
    /// </summary>
    /// <remarks>
    /// Separate from creating, because a partner sends contact details on every order: if
    /// creating also synced, the first order for an existing customer would overwrite details
    /// nobody asked to change.
    /// </remarks>
    Task<Result> SyncPartnerContactAsync(
        long customerId, long partnerId, PartnerContact contact, string correlationId,
        CancellationToken ct = default);

    Task<Result<SalesInvoiceParty>> GetInvoicePartyAsync(
        long customerId, CancellationToken ct = default);

    Task<Result<SalesWebsiteCustomer>> EnsureWebsiteCustomerAsync(
        Guid identityUserId, string email, bool emailVerified, CancellationToken ct = default);
}

public sealed record SalesCommerceLimits(
    long MinUsdMinor,
    long MaxUsdMinor,
    int BulkDefaultMax,
    int CompositionDeadlineMs,
    int CompositionMaxLines);

public interface ISalesSettingsGateway
{
    Task<(SellerBlock Seller, IReadOnlyList<string> RequiredBuyerFields, bool GstRegistered)>
        GetSellerAsync(CancellationToken ct = default);

    Task<PaymentInstructionBlock> GetActivePaymentInstructionAsync(CancellationToken ct = default);

    Task<SalesCommerceLimits> GetCommerceLimitsAsync(CancellationToken ct = default);
}

/// <summary>
/// Everything that can be composed into a basket in one currency, with the versions it was built
/// from.
/// </summary>
/// <remarks>
/// The versions are what make caching safe: a pool is reusable only while the catalogue and the
/// prices behind it are unchanged, and holding a stale pool would compose a basket from products
/// that have been withdrawn.
/// </remarks>
public sealed record CandidatePool(
    string Currency,
    long CatalogVersion,
    long PricingVersion,
    IReadOnlyList<CompositionCandidate> Candidates);

public interface ICandidatePoolGateway
{
    Task<Result<CandidatePool>> GetAsync(
        string currency, DateTimeOffset now, CancellationToken ct = default);

    void Invalidate(string currency);
}
