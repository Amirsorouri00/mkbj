using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Sales.Application;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.Application.Services;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Services;

/// <summary>
/// A batch of partner orders: isolation, bounded parallelism, and a deadline.
/// </summary>
/// <remarks>
/// The thing a batch must never do is let one item spoil another. These tests drive a stub
/// single-order service so the behaviour under test is the batching itself rather than the
/// composition engine running a hundred times.
/// </remarks>
public sealed class PartnerBulkOrderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PartnerPrincipal Principal = new(
        7, 1, new HashSet<string>(StringComparer.Ordinal) { PartnerScopes.OrdersCreate });

    private static IReadOnlyList<BulkItem> Items(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new BulkItem(
            i,
            new CreatePartnerOrderCommand($"po-{i}", $"c-{i}", 50_000, "USD", null)))];

    [Fact]
    public async Task Every_Item_Runs_In_Its_Own_Scope()
    {
        var (service, world, inner) = Build();

        await service.CreateBulkAsync(Principal, Items(5), "corr", Ct);

        // One shared DbContext across a batch would mean one poisoned change tracker failing
        // the rest, and one failed save rolling back its neighbours.
        world.Scopes.ScopesCreated.Should().Be(5);
        inner.Calls.Should().Be(5);
    }

    [Fact]
    public async Task Outcomes_Come_Back_In_The_Order_Submitted()
    {
        var (service, _, _) = Build();

        var outcomes = await service.CreateBulkAsync(Principal, Items(20), "corr", Ct);

        outcomes.Select(o => o.Index).Should().Equal(Enumerable.Range(0, 20));
        outcomes.Select(o => o.ExternalOrderId)
            .Should().Equal(Enumerable.Range(0, 20).Select(i => $"po-{i}"));
    }

    [Fact]
    public async Task No_More_Than_The_Configured_Number_Run_At_Once()
    {
        var (service, _, inner) = Build();
        inner.Hold = TimeSpan.FromMilliseconds(40);

        await service.CreateBulkAsync(Principal, Items(16), "corr", Ct);

        // Each item composes a basket; an unbounded batch of a hundred would starve every other
        // request on the machine.
        inner.PeakConcurrency.Should().BeLessThanOrEqualTo(4);
        inner.PeakConcurrency.Should().BeGreaterThan(1, "a batch that ran one at a time would be a loop");
    }

    [Fact]
    public async Task One_Items_Failure_Is_Its_Own()
    {
        var (service, _, inner) = Build();
        inner.FailAt = 3;

        var outcomes = await service.CreateBulkAsync(Principal, Items(6), "corr", Ct);

        var failed = outcomes.Single(o => o.Index == 3);
        failed.Status.Should().Be(BulkItemStatus.Failed);
        failed.Error!.Code.Should().Be("AMOUNT_OUT_OF_RANGE");

        outcomes.Where(o => o.Index != 3).Should().OnlyContain(
            o => o.Status == BulkItemStatus.Created,
            "a batch that abandoned the rest on one refusal would discard orders that were fine");
    }

    [Fact]
    public async Task One_Items_Exception_Becomes_That_Items_Failure()
    {
        var (service, _, inner) = Build();
        inner.ThrowAt = 2;

        var outcomes = await service.CreateBulkAsync(Principal, Items(5), "corr", Ct);

        var failed = outcomes.Single(o => o.Index == 2);
        failed.Status.Should().Be(BulkItemStatus.Failed);
        failed.Error!.Code.Should().Be("INTERNAL_SALES");

        outcomes.Count(o => o.Status == BulkItemStatus.Created).Should().Be(4,
            "an unexpected bug in one order must not discard ninety-nine good ones");
    }

    [Fact]
    public async Task A_Replay_Is_Reported_As_A_Replay()
    {
        var (service, _, inner) = Build();
        inner.ReplayAt = 1;

        var outcomes = await service.CreateBulkAsync(Principal, Items(3), "corr", Ct);

        outcomes.Single(o => o.Index == 1).Status.Should().Be(BulkItemStatus.Replayed);
    }

    [Fact]
    public async Task Items_Not_Started_Before_The_Deadline_Are_Retryable()
    {
        var (service, _, inner) = Build(deadlineSeconds: 0);
        inner.Hold = TimeSpan.FromMilliseconds(20);

        var outcomes = await service.CreateBulkAsync(Principal, Items(12), "corr", Ct);

        var abandoned = outcomes.Where(o => o.Error?.Code == "BATCH_DEADLINE_EXCEEDED").ToList();

        abandoned.Should().NotBeEmpty("a zero-second deadline has already passed");
        abandoned.Should().OnlyContain(o => o.Error!.Retryable,
            "nothing was attempted, so resubmitting is safe and may well work");
        abandoned.Should().OnlyContain(o => o.OrderId == null);
    }

    private static (IPartnerOrderService Service, SalesWorld World, StubOrders Inner) Build(
        int deadlineSeconds = 120)
    {
        var world = new SalesWorld();
        var inner = new StubOrders();
        world.Scopes.Register<IPartnerOrderService>(inner);

        var service = new PartnerOrderService(
            world.Orders,
            world.Pool,
            world.Pricing,
            world.Customers,
            world.Settings,
            new SalesWorld.FakeNumbers(),
            world.Attempts,
            world.Budgets,
            new StubSeeds(),
            new NonGstTaxPolicy(),
            new LiveRequoter(world.Catalog, world.Pricing),
            new StubIds(),
            new StubClock(),
            world.Scopes,
            Microsoft.Extensions.Options.Options.Create(new SalesOptions
            {
                BulkParallelism = 4,
                BulkDeadlineSeconds = deadlineSeconds,
            }),
            NullLogger<PartnerOrderService>.Instance);

        return (service, world, inner);
    }

    /// <summary>Stands in for the single-order service each batch item resolves.</summary>
    private sealed class StubOrders : IPartnerOrderService
    {
        private int _running;

        public int Calls { get; private set; }
        public int PeakConcurrency { get; private set; }
        public TimeSpan Hold { get; set; }
        public int? FailAt { get; set; }
        public int? ThrowAt { get; set; }
        public int? ReplayAt { get; set; }

        public async Task<Result<PartnerOrderCreated>> CreateAsync(
            PartnerPrincipal principal, CreatePartnerOrderCommand command, string correlationId,
            CancellationToken ct = default)
        {
            var running = Interlocked.Increment(ref _running);
            lock (this)
            {
                Calls++;
                PeakConcurrency = Math.Max(PeakConcurrency, running);
            }

            try
            {
                if (Hold > TimeSpan.Zero) await Task.Delay(Hold, ct);

                var index = int.Parse(
                    command.ExternalOrderId["po-".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);

                if (index == ThrowAt) throw new InvalidOperationException("Something broke.");

                if (index == FailAt)
                    return Result.Failure<PartnerOrderCreated>(SalesErrors.AmountOutOfRange);

                return Result.Success(new PartnerOrderCreated(
                    1_000 + index, Replayed: index == ReplayAt, []));
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        public Task<IReadOnlyList<BulkItemOutcome>> CreateBulkAsync(
            PartnerPrincipal principal, IReadOnlyList<BulkItem> items, string correlationId,
            CancellationToken ct = default) =>
            throw new NotSupportedException("A batch item never starts another batch.");
    }

    private sealed class StubSeeds : ICompositionSeedSource
    {
        public long NextSeed() => 1;
    }

    private sealed class StubIds : EwaysVNext.Domain.Common.IIdGenerator
    {
        private long _next;

        public long Generate() => Interlocked.Increment(ref _next);
    }

    private sealed class StubClock : EwaysVNext.Domain.Common.IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    }
}
