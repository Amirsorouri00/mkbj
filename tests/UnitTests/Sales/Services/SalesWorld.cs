using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Domain.Telemetry;
using EwaysVNext.Modules.Sales.Application.Money;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.Application.Services;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Logging.Abstractions;

namespace EwaysVNext.UnitTests.Sales.Services;

/// <summary>
/// A whole Sales world of fakes, sharing one journal so the <b>order</b> of its writes is
/// observable.
/// </summary>
/// <remarks>
/// Hand-written rather than mocked, because almost every test here asserts a sequence —
/// "composed, then created the customer, then inserted the order" — and a shared append-only
/// journal says that more plainly than received-call ordering ever does.
/// </remarks>
internal sealed class SalesWorld
{
    public SalesWorld(params string[] scopes)
    {
        Principal = new PartnerPrincipal(
            PartnerId: 7,
            ApiKeyId: 1,
            scopes.Length > 0
                ? new HashSet<string>(scopes, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal)
                {
                    PartnerScopes.OrdersCreate, PartnerScopes.CustomersWrite,
                });

        Catalog = new FakeCatalog(Journal);
        Pricing = new FakePricing();
        Customers = new FakeCustomers(Journal);
        Settings = new FakeSettings();
        Orders = new FakeOrders(Journal);
        Pool = new FakePool(Pricing, Journal);
        Attempts = new FakeAttempts();
        Budgets = new FakeBudgets();

        Service = new PartnerOrderService(
            Orders,
            Pool,
            Pricing,
            Customers,
            Settings,
            new FakeNumbers(),
            Attempts,
            Budgets,
            new FixedSeeds(),
            new NonGstTaxPolicy(),
            new LiveRequoter(Catalog, Pricing),
            new SequentialIds(),
            new FixedClock(),
            NullLogger<PartnerOrderService>.Instance);
    }

    public List<string> Journal { get; } = [];

    public PartnerPrincipal Principal { get; }
    public FakeCatalog Catalog { get; }
    public FakePricing Pricing { get; }
    public FakeCustomers Customers { get; }
    public FakeSettings Settings { get; }
    public FakeOrders Orders { get; }
    public FakePool Pool { get; }
    public FakeAttempts Attempts { get; }
    public FakeBudgets Budgets { get; }
    public IPartnerOrderService Service { get; }

    public static string FingerprintFor(CreatePartnerOrderCommand command) =>
        RequestFingerprint.Compute(
            command.ExternalCustomerId, command.AmountMinor, command.Currency);

    /// <summary>
    /// Ten candidates priced so the engine can hit 50,000 several ways.
    /// </summary>
    /// <remarks>
    /// Several ways on purpose: a pool with one solution would make the re-quote tests pass by
    /// luck when a dropped variant happened to be unused.
    /// </remarks>
    public static IReadOnlyList<CompositionCandidate> Candidates() =>
        [.. Enumerable.Range(1, 10).Select(i => new CompositionCandidate(
            VariantId: i,
            CategoryId: 100 + i,
            ReferenceMinor: i * 1_000,
            LoMinor: ((i * 1_000 * 95) + 99) / 100,
            HiMinor: i * 1_000 * 105 / 100))];

    internal sealed class FakeCatalog(List<string> journal) : ISalesCatalogGateway
    {
        /// <summary>A variant the pool still offers but the live read no longer does.</summary>
        public long? WithdrawOnLiveRead { get; set; }

        public bool WithdrawEverythingOnLiveRead { get; set; }

        public Task<IReadOnlyList<SalesVariant>> GetSellableAsync(
            IReadOnlyCollection<long>? variantIds, CancellationToken ct = default)
        {
            if (variantIds is not null) journal.Add("catalog:live");

            var ids = variantIds ?? [.. Candidates().Select(c => c.VariantId)];

            if (variantIds is not null && WithdrawEverythingOnLiveRead)
                return Task.FromResult<IReadOnlyList<SalesVariant>>([]);

            return Task.FromResult<IReadOnlyList<SalesVariant>>(
            [
                .. ids
                    .Where(id => variantIds is null || id != WithdrawOnLiveRead)
                    .Select(id => new SalesVariant(
                        id, id * 10, $"SKU-{id}", $"Product {id}", 1, "Demo Brand", 100 + id,
                        ["Kitchen"], new Dictionary<string, string>())),
            ]);
        }

        public Task<long> GetVersionAsync(CancellationToken ct = default) => Task.FromResult(1L);
    }

    internal sealed class FakePricing : ISalesPricingGateway
    {
        /// <summary>Moves the live reference away from the cached one.</summary>
        public long LiveReferenceShift { get; set; }

        public long? UsdOverride { get; set; }

        /// <summary>What the cached pool says, which <see cref="FakePool"/> builds directly.</summary>
        public static long CachedReferenceFor(long variantId) => variantId * 1_000;

        /// <remarks>
        /// Every call here is a <b>live</b> re-quote: the fake pool builds its candidates
        /// itself, so pricing is only ever asked about the chosen few. The shift therefore
        /// always applies — an earlier version of this fake tried to tell the two apart by call
        /// order and silently never shifted anything.
        /// </remarks>
        public Task<Result<IReadOnlyList<SalesPriceQuote>>> QuoteAsync(
            IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now,
            CancellationToken ct = default)
        {
            var shift = LiveReferenceShift;

            return Task.FromResult(Result.Success<IReadOnlyList<SalesPriceQuote>>(
            [
                .. variantIds.Select(id =>
                {
                    var reference = (id * 1_000) + shift;
                    return new SalesPriceQuote(
                        id,
                        reference,
                        ((reference * 95) + 99) / 100,
                        reference * 105 / 100,
                        new PriceSnapshot(
                            reference, LinePriceSource.Base, null, null, null, null, null,
                            reference, currency));
                }),
            ]));
        }

        public Task<Result<SalesUsdConversion>> ToUsdAsync(
            long amountMinor, string currency, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new SalesUsdConversion(UsdOverride ?? amountMinor)));

        public Task<long> GetVersionAsync(CancellationToken ct = default) => Task.FromResult(1L);
    }

    internal sealed class FakePool(FakePricing pricing, List<string> journal) : ICandidatePoolGateway
    {
        public int Calls { get; private set; }
        public List<string> Invalidations { get; } = [];

        public Task<Result<CandidatePool>> GetAsync(
            string currency, DateTimeOffset now, CancellationToken ct = default)
        {
            Calls++;
            journal.Add("pool:load");
            _ = pricing;

            return Task.FromResult(Result.Success(
                new CandidatePool(currency, 1, 1, Candidates())));
        }

        public void Invalidate(string currency) => Invalidations.Add(currency);
    }

    internal sealed class FakeCustomers(List<string> journal) : ISalesCustomerGateway
    {
        public SalesPartnerCustomer? Existing { get; set; } =
            new(42, "stored@example.com", "+6500000000", "Stored Name");

        public bool SyncThrows { get; set; }

        public int FindCalls { get; private set; }
        public List<long> Created { get; } = [];
        public List<long> Synced { get; } = [];

        public Task<SalesPartnerCustomer?> FindPartnerCustomerAsync(
            long partnerId, string externalCustomerId, CancellationToken ct = default)
        {
            FindCalls++;
            return Task.FromResult(Existing);
        }

        public Task<Result<SalesPartnerCustomer>> CreatePartnerCustomerAsync(
            long partnerId, string externalCustomerId, PartnerContact contact, string correlationId,
            CancellationToken ct = default)
        {
            journal.Add("customer:create");
            Created.Add(99);

            return Task.FromResult(Result.Success(
                new SalesPartnerCustomer(99, contact.Email, contact.Phone, contact.FullName)));
        }

        public Task<Result> SyncPartnerContactAsync(
            long customerId, long partnerId, PartnerContact contact, string correlationId,
            CancellationToken ct = default)
        {
            journal.Add("customer:sync");

            if (SyncThrows) throw new InvalidOperationException("Customer is unreachable.");

            Synced.Add(customerId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<SalesInvoiceParty>> GetInvoicePartyAsync(
            long customerId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new SalesInvoiceParty(
                customerId, Existing?.FullName, "Lim Trading", Existing?.Email, Existing?.Phone,
                "2 Buyer Street", null)));

        public Task<Result<SalesWebsiteCustomer>> EnsureWebsiteCustomerAsync(
            Guid identityUserId, string email, bool emailVerified, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new SalesWebsiteCustomer(42, emailVerified, true)));
    }

    internal sealed class FakeSettings : ISalesSettingsGateway
    {
        public IReadOnlyList<string> RequiredBuyerFields { get; set; } = ["email|phone"];

        public Task<(SellerBlock Seller, IReadOnlyList<string> RequiredBuyerFields, bool GstRegistered)>
            GetSellerAsync(CancellationToken ct = default) =>
            Task.FromResult((
                new SellerBlock("Demo Pte. Ltd.", "202612345A", "1 Example Road", "ar@example.com", "Thanks"),
                RequiredBuyerFields,
                false));

        public Task<PaymentInstructionBlock> GetActivePaymentInstructionAsync(
            CancellationToken ct = default) =>
            Task.FromResult(new PaymentInstructionBlock(9, 3, "Pay to …"));

        public Task<SalesCommerceLimits> GetCommerceLimitsAsync(CancellationToken ct = default) =>
            Task.FromResult(new SalesCommerceLimits(1_000, 100_000_000, 50, 5_000, 200));
    }

    internal sealed class FakeOrders(List<string> journal) : IOrderRepository
    {
        public PartnerOrderKey? Existing { get; set; }

        /// <summary>Simulates losing the insert race, then what the winner left behind.</summary>
        public PartnerOrderKey? RejectInsertThenReturn { get; set; }

        public List<Order> Inserted { get; } = [];

        private bool _rejected;

        public Task<Order?> GetByIdAsync(long orderId, CancellationToken ct = default) =>
            Task.FromResult<Order?>(null);

        public Task<PartnerOrderKey?> FindPartnerOrderKeyAsync(
            long partnerId, string externalOrderId, CancellationToken ct = default) =>
            Task.FromResult(_rejected ? RejectInsertThenReturn : Existing);

        public Task<bool> TryInsertPartnerOrderAsync(Order order, CancellationToken ct = default)
        {
            if (RejectInsertThenReturn is not null && !_rejected)
            {
                _rejected = true;
                return Task.FromResult(false);
            }

            journal.Add("order:insert");
            Inserted.Add(order);

            return Task.FromResult(true);
        }

        public Task AddAsync(Order order, CancellationToken ct = default) => Task.CompletedTask;

        public Task<DocumentForRendering?> FindDocumentAsync(
            long documentId, DocumentAccess access, CancellationToken ct = default) =>
            Task.FromResult<DocumentForRendering?>(null);
    }

    internal sealed class FakeNumbers : IDocumentNumberAllocator
    {
        private long _next;

        public Task<string> NextAsync(DocumentNumberKind kind, CancellationToken ct = default) =>
            Task.FromResult(kind == DocumentNumberKind.Order
                ? OrderNumberFormat.Order(++_next)
                : OrderNumberFormat.Document(DocumentType.Commercial, ++_next));
    }

    internal sealed class FakeAttempts : ICompositionAttemptRecorder
    {
        public List<CompositionAttemptRecord> Records { get; } = [];

        public Task RecordAsync(CompositionAttemptRecord record, CancellationToken ct = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeBudgets : ICompositionBudgetFactory
    {
        public bool Expired { get; set; }

        public ICompositionBudget Start(TimeSpan deadline) =>
            Expired ? new ExpiredBudget() : new IterationBudget(500_000);
    }

    private sealed class FixedSeeds : ICompositionSeedSource
    {
        public long NextSeed() => 20261107;
    }

    private sealed class SequentialIds : IIdGenerator
    {
        private long _next = 1_000;

        public long Generate() => Interlocked.Increment(ref _next);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    }
}
