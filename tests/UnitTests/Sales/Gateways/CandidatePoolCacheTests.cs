using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Adapter.Outbound.Candidates;
using EwaysVNext.Modules.Sales.Application;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Gateways;

/// <summary>
/// The pool of variants a basket is composed from, and when it may be reused.
/// </summary>
/// <remarks>
/// Building it means reading every sellable variant and quoting all of them, which is the
/// expensive part of placing an order — but a pool that outlives its truth sells withdrawn
/// products. The versions are what let it be both cached and correct, and these tests are mostly
/// about the moments it must <b>not</b> be reused.
/// </remarks>
public sealed class CandidatePoolCacheTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Second_Call_Within_The_Ttl_With_Unchanged_Versions_Hits_The_Cache()
    {
        var (gateway, catalog, _) = Build();

        await gateway.GetAsync("USD", T0);
        await gateway.GetAsync("USD", T0.AddSeconds(29));

        catalog.SellableCalls.Should().Be(1);
    }

    [Fact]
    public async Task Expires_At_The_Ttl_Boundary()
    {
        var (gateway, catalog, _) = Build();

        await gateway.GetAsync("USD", T0);
        await gateway.GetAsync("USD", T0.AddSeconds(30));

        // Exclusive: "a 30 second cache" that still answered at exactly 30 seconds would be a
        // 30-seconds-plus-one-request cache.
        catalog.SellableCalls.Should().Be(2);
    }

    [Fact]
    public async Task A_Catalogue_Change_Reloads_Immediately()
    {
        var (gateway, catalog, _) = Build();

        await gateway.GetAsync("USD", T0);
        catalog.Version = 2;
        await gateway.GetAsync("USD", T0.AddSeconds(1));

        catalog.SellableCalls.Should().Be(2,
            "a product withdrawn a second ago must not be composed into an order");
    }

    [Fact]
    public async Task A_Price_Change_Reloads_Immediately()
    {
        var (gateway, catalog, pricing) = Build();

        await gateway.GetAsync("USD", T0);
        pricing.Version = 2;
        await gateway.GetAsync("USD", T0.AddSeconds(1));

        catalog.SellableCalls.Should().Be(2);
    }

    [Fact]
    public async Task Invalidate_Forces_A_Reload()
    {
        var (gateway, catalog, _) = Build();

        await gateway.GetAsync("USD", T0);
        gateway.Invalidate("USD");
        await gateway.GetAsync("USD", T0.AddSeconds(1));

        catalog.SellableCalls.Should().Be(2);
    }

    [Fact]
    public async Task The_Cache_Is_Per_Currency()
    {
        var (gateway, catalog, _) = Build();

        await gateway.GetAsync("USD", T0);
        await gateway.GetAsync("EUR", T0);

        catalog.SellableCalls.Should().Be(2, "prices differ by currency, so pools do too");
    }

    [Fact]
    public async Task A_Variant_Without_A_Quote_Is_Not_A_Candidate()
    {
        var (gateway, _, pricing) = Build();
        pricing.OmitVariantId = 3;

        var pool = (await gateway.GetAsync("USD", T0)).Value!;

        pool.Candidates.Select(candidate => candidate.VariantId).Should().NotContain(3,
            "the quote port omits what it cannot price, and composing with it would mean "
            + "inventing a price the shop cannot offer");
    }

    [Fact]
    public async Task A_Candidate_Takes_Its_Category_From_The_Catalogue_And_Its_Band_From_The_Quote()
    {
        var (gateway, _, _) = Build();

        var candidate = (await gateway.GetAsync("USD", T0)).Value!.Candidates
            .Single(c => c.VariantId == 1);

        candidate.CategoryId.Should().Be(101);
        candidate.ReferenceMinor.Should().Be(1_000);
        candidate.LoMinor.Should().Be(950);
        candidate.HiMinor.Should().Be(1_050);
    }

    [Fact]
    public async Task A_Currency_The_Shop_Does_Not_Sell_In_Is_Returned_And_Not_Cached()
    {
        var (gateway, catalog, pricing) = Build();
        pricing.Failure = new Error("CURRENCY_NOT_ENABLED", "No.", 422);

        var first = await gateway.GetAsync("ZZZ", T0);
        first.IsFailure.Should().BeTrue();
        first.Error!.Code.Should().Be("CURRENCY_NOT_ENABLED");

        pricing.Failure = null;
        await gateway.GetAsync("ZZZ", T0);

        catalog.SellableCalls.Should().Be(2, "a failure is not a pool to remember");
    }

    private static (CachedCandidatePoolGateway Gateway, FakeCatalog Catalog, FakePricing Pricing) Build()
    {
        var catalog = new FakeCatalog();
        var pricing = new FakePricing();
        var cache = new CandidatePoolCache();

        return (
            new CachedCandidatePoolGateway(
                catalog, pricing, cache, Options.Create(new SalesOptions())),
            catalog,
            pricing);
    }

    private sealed class FakeCatalog : ISalesCatalogGateway
    {
        public int SellableCalls { get; private set; }
        public long Version { get; set; } = 1;

        public Task<IReadOnlyList<SalesVariant>> GetSellableAsync(
            IReadOnlyCollection<long>? variantIds, CancellationToken ct = default)
        {
            SellableCalls++;

            return Task.FromResult<IReadOnlyList<SalesVariant>>(
            [
                Variant(1, 101),
                Variant(2, 102),
                Variant(3, 103),
            ]);
        }

        public Task<long> GetVersionAsync(CancellationToken ct = default) => Task.FromResult(Version);

        private static SalesVariant Variant(long id, long categoryId) => new(
            id, id * 10, $"SKU-{id}", $"Product {id}", 1, "Demo Brand", categoryId,
            ["Kitchen"], new Dictionary<string, string>());
    }

    private sealed class FakePricing : ISalesPricingGateway
    {
        public long Version { get; set; } = 1;
        public long? OmitVariantId { get; set; }
        public Error? Failure { get; set; }

        public Task<Result<IReadOnlyList<SalesPriceQuote>>> QuoteAsync(
            IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now,
            CancellationToken ct = default)
        {
            if (Failure is not null)
                return Task.FromResult(Result.Failure<IReadOnlyList<SalesPriceQuote>>(Failure));

            return Task.FromResult(Result.Success<IReadOnlyList<SalesPriceQuote>>(
            [
                .. variantIds
                    .Where(id => id != OmitVariantId)
                    .Select(id => new SalesPriceQuote(
                        id, id * 1_000, id * 950, id * 1_050,
                        new PriceSnapshot(
                            id * 1_000, LinePriceSource.Base, null, null, null, null, null,
                            id * 1_000, currency))),
            ]));
        }

        public Task<Result<SalesUsdConversion>> ToUsdAsync(
            long amountMinor, string currency, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new SalesUsdConversion(amountMinor)));

        public Task<long> GetVersionAsync(CancellationToken ct = default) => Task.FromResult(Version);
    }
}
