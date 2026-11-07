using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Carts;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.Application.Services;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Services;

/// <summary>
/// The cart service: who may use it, and what it does when the shop moves underneath one.
/// </summary>
/// <remarks>
/// The behaviour worth testing here is not "add puts a line in the cart" — the aggregate's own
/// tests cover that. It is the part the service alone owns: every operation re-quotes live, and
/// a price that moved or a product that was withdrawn is <b>reported</b> to the customer rather
/// than applied in silence.
/// </remarks>
public sealed class CartServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ShopCaller Caller =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "buyer@example.test", true);

    [Fact]
    public async Task A_Suspended_Account_Cannot_Touch_Its_Cart()
    {
        var world = new CartWorld();
        world.Customers.WebsiteActive = false;

        var result = await world.Service.AddAsync(Caller, 10, 1, Ct);

        result.Error!.Code.Should().Be("CUSTOMER_SUSPENDED");
        world.Saves.Should().Be(0, "a refusal writes nothing");
    }

    [Fact]
    public async Task Adding_Something_Unsellable_Is_Not_Found()
    {
        var world = new CartWorld();
        world.Catalog.WithdrawEverythingOnLiveRead = true;

        var result = await world.Service.AddAsync(Caller, 10, 1, Ct);

        result.Error!.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task A_New_Cart_Is_In_Dollars_And_Priced_Live()
    {
        var world = new CartWorld();

        var cart = (await world.Service.AddAsync(Caller, 10, 2, Ct)).Value!;

        cart.Currency.Should().Be("USD");
        var line = cart.Lines.Should().ContainSingle().Subject;
        line.UnitPriceMinor.Should().Be(10_000, "the live quote is what the customer is shown");
        line.Sku.Should().Be("SKU-10");
        cart.TotalMinor.Should().Be(20_000);
        cart.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Reading_The_Cart_After_A_Price_Move_Reports_It_And_Persists_The_New_Price()
    {
        var world = new CartWorld();
        await world.Service.AddAsync(Caller, 10, 2, Ct);

        world.Pricing.LiveReferenceShift = 500;

        var cart = (await world.Service.GetAsync(Caller, Ct)).Value!;

        cart.Changes.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CartChange(10, CartChange.Repriced, 10_000, 10_500));
        cart.Lines.Single().UnitPriceMinor.Should().Be(10_500);

        world.Stored!.Lines.Single().DisplayedUnitPriceMinor.Should().Be(10_500,
            "checkout compares against what was shown, so what was shown must be saved");
    }

    [Fact]
    public async Task A_Withdrawn_Product_Leaves_The_Cart_And_Is_Reported()
    {
        var world = new CartWorld();
        await world.Service.AddAsync(Caller, 10, 1, Ct);
        await world.Service.AddAsync(Caller, 20, 1, Ct);

        world.Catalog.WithdrawOnLiveRead = 20;

        var cart = (await world.Service.GetAsync(Caller, Ct)).Value!;

        cart.Lines.Select(line => line.VariantId).Should().Equal(10);
        cart.Changes.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CartChange(20, CartChange.Removed, 20_000, null));
    }

    [Fact]
    public async Task Switching_Currency_Requotes_Every_Line()
    {
        var world = new CartWorld();
        await world.Service.AddAsync(Caller, 10, 1, Ct);

        var cart = (await world.Service.SetCurrencyAsync(Caller, "EUR", Ct)).Value!;

        cart.Currency.Should().Be("EUR");
        world.Pricing.LastCurrency.Should().Be("EUR");
        world.Stored!.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task A_Quantity_Of_A_Thousand_Is_A_Validation_Failure_Not_An_Exception()
    {
        var world = new CartWorld();

        var result = await world.Service.AddAsync(Caller, 10, 1_000, Ct);

        // The aggregate throws; a Result-returning method must not (rule 9).
        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        result.Error.HttpStatus.Should().Be(400);
    }

    /// <summary>The smallest world a cart needs: a repository, a clock, and a save counter.</summary>
    private sealed class CartWorld
    {
        public CartWorld()
        {
            Service = new CartServiceFactory(this).Build();
        }

        public SalesWorld.FakeCatalog Catalog { get; } = new([]);
        public CountingPricing Pricing { get; } = new();
        public SalesWorld.FakeCustomers Customers { get; } = new([]);
        public ICartService Service { get; }

        public Cart? Stored { get; set; }
        public int Saves { get; set; }
    }

    /// <summary>Remembers the currency it was last asked about.</summary>
    private sealed class CountingPricing : ISalesPricingGateway
    {
        private readonly SalesWorld.FakePricing _inner = new();

        public long LiveReferenceShift
        {
            get => _inner.LiveReferenceShift;
            set => _inner.LiveReferenceShift = value;
        }

        public string? LastCurrency { get; private set; }

        public Task<Result<IReadOnlyList<SalesPriceQuote>>> QuoteAsync(
            IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now,
            CancellationToken ct = default)
        {
            LastCurrency = currency;
            return _inner.QuoteAsync(variantIds, currency, now, ct);
        }

        public Task<Result<SalesUsdConversion>> ToUsdAsync(
            long amountMinor, string currency, DateTimeOffset now, CancellationToken ct = default) =>
            _inner.ToUsdAsync(amountMinor, currency, now, ct);

        public Task<long> GetVersionAsync(CancellationToken ct = default) =>
            _inner.GetVersionAsync(ct);
    }

    private sealed class FakeCarts(CartWorld world) : ICartRepository
    {
        public Task<Cart?> GetByCustomerAsync(long customerId, CancellationToken ct = default) =>
            Task.FromResult(world.Stored);

        public Task AddAsync(Cart cart, CancellationToken ct = default)
        {
            world.Stored = cart;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingUnitOfWork(CartWorld world) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            world.Saves++;
            return Task.FromResult(1);
        }

        public int SaveChanges() => throw new NotSupportedException();

        public Task<SaveOutcome> TrySaveChangesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class CartServiceFactory(CartWorld world)
    {
        public ICartService Build() => new CartService(
            new FakeCarts(world),
            world.Catalog,
            world.Pricing,
            world.Customers,
            new CountingUnitOfWork(world),
            new SequentialIds(),
            new FixedClock());
    }

    private sealed class SequentialIds : IIdGenerator
    {
        private long _next = 5_000;

        public long Generate() => Interlocked.Increment(ref _next);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    }
}
