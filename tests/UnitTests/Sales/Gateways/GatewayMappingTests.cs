using AwesomeAssertions;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Sales.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;
using EwaysVNext.Outcomes;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Gateways;

/// <summary>
/// The anti-corruption layer, field by field.
/// </summary>
/// <remarks>
/// The gateways exist so another module's types stop at the boundary. That only helps if the
/// mapping is right — a silently dropped FX rate id or a mis-mapped price source would make an
/// invoice unexplainable, and nothing downstream would notice because the shape would still be
/// valid.
/// </remarks>
public sealed class GatewayMappingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_Sellable_Variant_Maps_Field_For_Field()
    {
        var gateway = new SalesCatalogGateway(new StubSellable());

        var variant = (await gateway.GetSellableAsync(null, Ct)).Should().ContainSingle().Which;

        variant.VariantId.Should().Be(1);
        variant.ProductId.Should().Be(10);
        variant.Sku.Should().Be("SKU-1");
        variant.ProductTitle.Should().Be("A kettle", "Catalog calls it Title; an order calls it the product title");
        variant.BrandId.Should().Be(5);
        variant.BrandName.Should().Be("Demo Brand");
        variant.PrimaryCategoryId.Should().Be(101);
        variant.CategoryPath.Should().Equal("Kitchen", "Kettles");
        variant.Attributes.Should().ContainKey("colour").WhoseValue.Should().Be("Red");
    }

    [Theory]
    [InlineData(PriceSource.Base, LinePriceSource.Base)]
    [InlineData(PriceSource.Override, LinePriceSource.Override)]
    [InlineData(PriceSource.Fx, LinePriceSource.Fx)]
    public async Task Every_Price_Source_Maps(PriceSource source, LinePriceSource expected)
    {
        var gateway = new SalesPricingGateway(new StubQuotes(source), new StubFx());

        var quote = (await gateway.QuoteAsync([1], "USD", DateTimeOffset.UtcNow, Ct))
            .Value!.Should().ContainSingle().Which;

        quote.Snapshot.Source.Should().Be(expected);
    }

    [Fact]
    public async Task An_Fx_Quote_Keeps_Everything_Needed_To_Explain_It()
    {
        var gateway = new SalesPricingGateway(new StubQuotes(PriceSource.Fx), new StubFx());

        var quote = (await gateway.QuoteAsync([1], "USD", DateTimeOffset.UtcNow, Ct))
            .Value!.Single();

        quote.ReferenceMinor.Should().Be(10_812);
        quote.LoMinor.Should().Be(10_272);
        quote.HiMinor.Should().Be(11_352);

        var snapshot = quote.Snapshot;
        snapshot.FxRate.Should().Be(1.0812m);
        snapshot.FxRateId.Should().Be(77);
        snapshot.FxOverrideId.Should().BeNull();
        snapshot.FxProvenance.Should().Be("ecb");
        snapshot.BaseCurrency.Should().Be("EUR", "the base is where the conversion came from");
    }

    [Fact]
    public async Task A_Base_Priced_Quote_Carries_The_Asked_For_Currency()
    {
        var gateway = new SalesPricingGateway(new StubQuotes(PriceSource.Base), new StubFx());

        var quote = (await gateway.QuoteAsync([1], "USD", DateTimeOffset.UtcNow, Ct))
            .Value!.Single();

        // No conversion happened, so there is no base to point at but the currency itself.
        quote.Snapshot.BaseCurrency.Should().Be("USD");
        quote.Snapshot.FxRate.Should().BeNull();
    }

    [Theory]
    [InlineData("1 Example Road", null, "Singapore", "049909", "SG", "1 Example Road, Singapore 049909, SG")]
    [InlineData("1 Example Road", "Unit 2", "Singapore", "049909", "SG", "1 Example Road, Unit 2, Singapore 049909, SG")]
    [InlineData("1 Example Road", "   ", "Singapore", "049909", "SG", "1 Example Road, Singapore 049909, SG")]
    public async Task A_Billing_Address_Flattens_To_The_Line_An_Invoice_Prints(
        string line1, string? line2, string city, string postal, string country, string expected)
    {
        var gateway = new SalesCustomerGateway(
            new StubResolution(new AddressSnapshot(line1, line2, city, postal, country)),
            new StubWebsite());

        var party = (await gateway.GetInvoicePartyAsync(42, Ct)).Value!;

        // Flattened here rather than in the renderer: the order stores the buyer block as it was,
        // and a structured address stored today would need formatting by whatever renderer exists
        // in three years.
        party.BillingAddress.Should().Be(expected);
    }

    [Fact]
    public async Task A_Customer_With_No_Address_Maps_To_No_Address()
    {
        var gateway = new SalesCustomerGateway(new StubResolution(null), new StubWebsite());

        (await gateway.GetInvoicePartyAsync(42, Ct)).Value!.BillingAddress.Should().BeNull();
    }

    [Fact]
    public async Task The_Seller_Block_And_Its_Requirements_Map()
    {
        var gateway = new SalesSettingsGateway(new StubSettings());

        var (seller, required, gstRegistered) = await gateway.GetSellerAsync(Ct);

        seller.LegalName.Should().Be("Demo Pte. Ltd.");
        seller.Uen.Should().Be("202612345A");
        seller.Footer.Should().Be("Thank you", "Settings calls it the invoice footer");
        required.Should().Equal("email|phone");
        gstRegistered.Should().BeFalse();
    }

    [Fact]
    public async Task Commerce_Limits_Map_Without_The_Fx_Age()
    {
        var gateway = new SalesSettingsGateway(new StubSettings());

        var limits = await gateway.GetCommerceLimitsAsync(Ct);

        limits.MinUsdMinor.Should().Be(1_000);
        limits.MaxUsdMinor.Should().Be(100_000_000);
        limits.BulkDefaultMax.Should().Be(50);
        limits.CompositionDeadlineMs.Should().Be(5_000);
        limits.CompositionMaxLines.Should().Be(200);
    }

    private sealed class StubSellable : ISellableVariantPort
    {
        public Task<IReadOnlyList<SellableVariant>> GetSellableAsync(
            IReadOnlyCollection<long>? variantIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SellableVariant>>(
            [
                new SellableVariant(
                    1, 10, "SKU-1", "A kettle", 5, "Demo Brand", 101,
                    ["Kitchen", "Kettles"],
                    new Dictionary<string, string> { ["colour"] = "Red" }),
            ]);

        public Task<long> GetVersionAsync(CancellationToken ct = default) => Task.FromResult(1L);
    }

    private sealed class StubQuotes(PriceSource source) : IPriceQuotePort
    {
        public Task<Result<IReadOnlyDictionary<long, PriceQuote>>> QuoteAsync(
            IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now,
            CancellationToken ct = default)
        {
            var fx = source == PriceSource.Fx
                ? new FxSnapshot(77, null, "EUR", currency, 1.0812m, now, "ecb")
                : null;

            return Task.FromResult(Result.Success<IReadOnlyDictionary<long, PriceQuote>>(
                new Dictionary<long, PriceQuote>
                {
                    [1] = new(1, currency, 10_812, 10_272, 11_352, source, fx),
                }));
        }

        public Task<long> GetVersionAsync(CancellationToken ct = default) => Task.FromResult(1L);
    }

    private sealed class StubFx : IFxSnapshotPort
    {
        public Task<Result<UsdConversion>> ToUsdAsync(
            long amountMinor, string currency, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new UsdConversion(amountMinor, null)));
    }

    private sealed class StubResolution(AddressSnapshot? address) : ICustomerResolutionPort
    {
        public Task<PartnerCustomerRef?> FindPartnerCustomerAsync(
            long partnerId, string externalCustomerId, CancellationToken ct = default) =>
            Task.FromResult<PartnerCustomerRef?>(new PartnerCustomerRef(42, "a@b.c", null, "Pat"));

        public Task<Result<PartnerCustomerRef>> CreatePartnerCustomerAsync(
            long partnerId, string externalCustomerId, ContactInput contact, string correlationId,
            CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new PartnerCustomerRef(42, contact.Email, contact.Phone, contact.FullName)));

        public Task<Result> SyncPartnerContactAsync(
            long customerId, long partnerId, ContactInput contact, string correlationId,
            CancellationToken ct = default) => Task.FromResult(Result.Success());

        public Task<Result<InvoiceParty>> GetInvoicePartyAsync(
            long customerId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new InvoiceParty(
                customerId, "Pat Lim", "Lim Trading", "pat@example.com", null, address, "RN-1")));
    }

    private sealed class StubWebsite : IWebsiteCustomerPort
    {
        public Task<Result<WebsiteCustomerRef>> EnsureFromIdentityAsync(
            Guid identityUserId, string email, bool emailVerified, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new WebsiteCustomerRef(42, emailVerified, true)));
    }

    private sealed class StubSettings : ISettingsPort
    {
        public Task<SellerSnapshot> GetSellerAsync(CancellationToken ct = default) =>
            Task.FromResult(new SellerSnapshot(
                "Demo Pte. Ltd.", "202612345A", "1 Example Road", "ar@example.com",
                GstRegistered: false, "Thank you", ["email|phone"]));

        public Task<PaymentInstructionSnapshot> GetActivePaymentInstructionAsync(
            CancellationToken ct = default) =>
            Task.FromResult(new PaymentInstructionSnapshot(9, 3, "Pay to …"));

        public Task<CommerceLimitsSnapshot> GetCommerceLimitsAsync(CancellationToken ct = default) =>
            Task.FromResult(new CommerceLimitsSnapshot(
                1_000, 100_000_000, 50, 5_000, 200, TimeSpan.FromHours(72)));
    }
}
