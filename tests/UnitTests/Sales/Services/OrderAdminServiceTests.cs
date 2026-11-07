using AwesomeAssertions;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.Application.Services;
using EwaysVNext.Persistence;
using EwaysVNext.UnitTests.Sales.Orders;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Services;

/// <summary>
/// The administrator's acts on an order: who may make them, and what they leave behind.
/// </summary>
/// <remarks>
/// The super-admin gate is tested here rather than only over HTTP for a reason: the route policy
/// refuses a staff token before the service is reached, so an integration test would pass with
/// the service check deleted. This is the only place that failure is visible.
/// </remarks>
public sealed class OrderAdminServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly AdminActor Staff = new(Guid.NewGuid(), IsSuperAdmin: false);
    private static readonly AdminActor Super = new(Guid.NewGuid(), IsSuperAdmin: true);

    [Fact]
    public async Task A_Staff_Account_Cannot_Refund_Even_With_The_Route_Open()
    {
        var world = new AdminWorld(OrderMother.PaidWebsiteOrder());

        var result = await world.Service.RecordRefundAsync(1, 100, "Why", null, Staff, Ct);

        result.Error!.Code.Should().Be("FORBIDDEN");
        world.Loads.Should().Be(0, "the order is not even read, so nothing leaks through timing");
        world.Saves.Should().Be(0);
        world.Audited.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Refund_Is_Audited_In_The_Same_Save_As_The_Change()
    {
        var world = new AdminWorld(OrderMother.PaidWebsiteOrder());

        var result = await world.Service.RecordRefundAsync(1, 1_000, "Damaged", "RTN-1", Super, Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value!.PaymentStatus.Should().Be("PartiallyRefunded");
        result.Value.RefundedMinor.Should().Be(1_000);
        result.Value.CreditNoteNumber.Should().StartWith("CN-");

        world.Saves.Should().Be(1, "one operation, one save");
        world.Audited.Should().Equal("sales.order.refund");
        // The audit entry commits with the change: an order that moved with no record of who
        // moved it is worse than one that did not move.
        world.Journal.Should().Equal("audit", "save");
    }

    [Fact]
    public async Task An_Unknown_Order_Is_Not_Found_And_Domain_Refusals_Keep_Their_Codes()
    {
        var missing = new AdminWorld(null);
        (await missing.Service.CancelAsync(1, Staff, Ct)).Error!.Code.Should().Be("NOT_FOUND");

        // Paid: cancelling is the wrong verb, and the code says so rather than "failed".
        var paid = new AdminWorld(OrderMother.PaidWebsiteOrder());
        (await paid.Service.CancelAsync(1, Staff, Ct)).Error!.Code
            .Should().Be("ORDER_STATE_CONFLICT");
        paid.Saves.Should().Be(0);

        var over = new AdminWorld(OrderMother.PaidWebsiteOrder());
        (await over.Service.RecordRefundAsync(1, 9_999, "Too much", null, Super, Ct)).Error!.Code
            .Should().Be("REFUND_EXCEEDS_REFUNDABLE");
    }

    [Fact]
    public async Task Confirming_Payment_Invoices_And_Audits_Once()
    {
        var world = new AdminWorld(OrderMother.WebsiteOrder());

        var result = await world.Service.ConfirmExternalPaymentAsync(
            1, "bank-transfer", "TT-1", Staff, Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        world.Audited.Should().Equal("sales.order.confirm_payment");
        world.Saves.Should().Be(1);
    }

    [Fact]
    public async Task A_Payment_With_No_Method_Is_Refused_Before_Anything_Is_Read()
    {
        var world = new AdminWorld(OrderMother.WebsiteOrder());

        var result = await world.Service.ConfirmExternalPaymentAsync(1, "  ", null, Staff, Ct);

        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        world.Loads.Should().Be(0);
    }

    /// <summary>One order, a counting save, and a journal that records the order of things.</summary>
    private sealed class AdminWorld
    {
        public AdminWorld(Order? order)
        {
            Service = new OrderAdminService(
                new OneOrder(this, order),
                new SalesWorld.FakeNumbers(),
                new SalesWorld.FakeSettings(),
                new NonGstTaxPolicy(),
                new RecordingAudit(this),
                new CountingUnitOfWork(this),
                new SequentialIds(),
                new FixedClock());
        }

        public OrderAdminService Service { get; }

        public List<string> Journal { get; } = [];
        public List<string> Audited { get; } = [];
        public int Loads { get; set; }
        public int Saves { get; set; }
    }

    private sealed class OneOrder(AdminWorld world, Order? order) : IOrderRepository
    {
        public Task<Order?> GetByIdAsync(long orderId, CancellationToken ct = default)
        {
            world.Loads++;
            return Task.FromResult(order);
        }

        public Task<PartnerOrderKey?> FindPartnerOrderKeyAsync(
            long partnerId, string externalOrderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TryInsertPartnerOrderAsync(Order order, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(Order order, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<DocumentForRendering?> FindDocumentAsync(
            long documentId, DocumentAccess access, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAudit(AdminWorld world) : IAuditTrail
    {
        public void Record(
            AuditActor actor, string action, string targetType, string targetId,
            object? before, object? after)
        {
            world.Audited.Add(action);
            world.Journal.Add("audit");
        }
    }

    private sealed class CountingUnitOfWork(AdminWorld world) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            world.Saves++;
            world.Journal.Add("save");
            return Task.FromResult(1);
        }

        public int SaveChanges() => throw new NotSupportedException();

        public Task<SaveOutcome> TrySaveChangesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class SequentialIds : IIdGenerator
    {
        private long _next = 70_000;

        public long Generate() => Interlocked.Increment(ref _next);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    }
}
