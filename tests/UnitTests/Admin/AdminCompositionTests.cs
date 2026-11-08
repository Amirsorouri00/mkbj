using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Admin.Application.Ports.Outbound;
using EwaysVNext.Modules.Admin.Application.Services;
using EwaysVNext.Modules.Admin.Application.Views;
using EwaysVNext.Outcomes;
using Xunit;

namespace EwaysVNext.UnitTests.Admin;

/// <summary>
/// What Admin itself decides, as opposed to what it copies.
/// </summary>
/// <remarks>
/// Most of Admin is mapping, and mapping is checked where it can actually break — the gateway
/// tests, against the real provider types. These are the few places Admin has a rule of its own:
/// how failure codes are rolled up, and what happens when one half of a composition is missing.
/// </remarks>
public sealed class AdminCompositionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_Dashboard_Rolls_Failure_Codes_Up_By_Count_Then_Name()
    {
        var sales = new FakeSales
        {
            Failures =
            [
                Failure("COMPOSITION_TIMEOUT"),
                Failure("EXACT_COMPOSITION_UNAVAILABLE"),
                Failure("COMPOSITION_TIMEOUT"),
                Failure("AMOUNT_OUT_OF_RANGE"),
                Failure("AMOUNT_OUT_OF_RANGE"),
            ],
        };

        var dashboard = await new AdminDashboardService(
            sales, new FakeSourcing(), new FixedClock()).GetAsync(Ct);

        dashboard.GeneratedAt.Should().Be(Now, "a dashboard without a time is one nobody can date");

        // Most frequent first; equal counts fall back to the name so the order does not shuffle
        // between refreshes.
        dashboard.RecentFailureCodes.Should().Equal(
            new FailureCodeCount("AMOUNT_OUT_OF_RANGE", 2),
            new FailureCodeCount("COMPOSITION_TIMEOUT", 2),
            new FailureCodeCount("EXACT_COMPOSITION_UNAVAILABLE", 1));

        dashboard.Sales.RecentFailures.Should().HaveCount(5, "the detail is kept beside the roll-up");

        // The import section is composed in, and its window is the dashboard's decision.
        dashboard.Imports.JobsQueued.Should().Be(1);
        dashboard.Imports.RecordsPublished7d.Should().Be(9);
    }

    [Fact]
    public async Task An_Order_Is_Readable_Even_When_Its_Customer_Record_Is_Gone()
    {
        var sales = new FakeSales();
        var customers = new FakeCustomers { Error = new Error("NOT_FOUND", "gone", 404) };

        var sheet = await new Order360Service(sales, customers).GetAsync(42, Ct);

        // The order is a real sale with real documents. Refusing to show it because the buyer's
        // record has since gone would hide the evidence exactly when someone is looking for it.
        sheet.IsSuccess.Should().BeTrue();
        sheet.Value!.Order.Summary.Id.Should().Be(42);
        sheet.Value.Customer.Should().BeNull();
    }

    [Fact]
    public async Task A_Missing_Order_Keeps_Its_Status_And_Asks_No_Further()
    {
        var sales = new FakeSales { OrderError = new Error("NOT_FOUND", "no such order", 404) };
        var customers = new FakeCustomers();

        var result = await new Order360Service(sales, customers).GetAsync(42, Ct);

        result.Error!.HttpStatus.Should().Be(404);
        customers.Calls.Should().Be(0, "there is nobody to look up for an order that is not there");
    }

    [Fact]
    public async Task A_Missing_Customer_Fails_The_360_Before_Any_Orders_Are_Fetched()
    {
        var sales = new FakeSales();
        var customers = new FakeCustomers { Error = new Error("NOT_FOUND", "gone", 404) };

        var result = await new Customer360Service(customers, sales).GetAsync(7, 20, Ct);

        result.Error!.HttpStatus.Should().Be(404);
        sales.OrderListCalls.Should().Be(0, "there is no useful 360 of a customer who is not there");
    }

    private static CompositionFailure Failure(string code) =>
        new(Now, code, "USD", 100_000, 12, 7);

    private sealed class FakeSales : ISalesReadPort
    {
        public IReadOnlyList<CompositionFailure> Failures { get; init; } = [];

        public Error? OrderError { get; init; }

        public int OrderListCalls { get; private set; }

        public Task<SalesSnapshot> GetDashboardAsync(
            DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(new SalesSnapshot(
                [],
                new PendingPayments(0, [], null),
                new CompositionWindow("24h", 0, 0, 0m, null),
                new CompositionWindow("7d", 0, 0, 0m, null),
                Failures));

        public Task<IReadOnlyList<OrderRow>> ListCustomerOrdersAsync(
            long customerId, int limit, CancellationToken ct = default)
        {
            OrderListCalls++;
            return Task.FromResult<IReadOnlyList<OrderRow>>([]);
        }

        public Task<Result<OrderSheet>> GetOrderAsync(
            long orderId, CancellationToken ct = default) =>
            Task.FromResult(OrderError is null
                ? Result.Success(new OrderSheet(
                    new OrderRow(
                        orderId, "ORD-000001", "Website", null, null, CustomerId: 7, "USD",
                        4_000, 0,
                        new OrderStatusView("Completed", "Paid", "AdminConfirmed", "Fulfilled",
                            "SystemAutomatic", "statement", "Invoiced"),
                        Now),
                    null, 4_000, "corr", LineCount: 2, [], [], []))
                : Result.Failure<OrderSheet>(OrderError));
    }

    private sealed class FakeSourcing : ISourcingReadPort
    {
        public DateTimeOffset? AskedSince { get; private set; }

        public Task<ImportSnapshot> GetDashboardCountsAsync(
            DateTimeOffset since, CancellationToken ct = default)
        {
            AskedSince = since;
            return Task.FromResult(new ImportSnapshot(1, 2, 3, 4, 5, 6, 7, 8, 9));
        }
    }

    private sealed class FakeCustomers : ICustomerReadPort
    {
        public Error? Error { get; init; }

        public int Calls { get; private set; }

        public Task<Result<CustomerProfile>> GetAsync(
            long customerId, CancellationToken ct = default)
        {
            Calls++;

            return Task.FromResult(Error is null
                ? Result.Success(new CustomerProfile(
                    customerId, "Website", null, null, "a@example.test", null, "Pat", null,
                    "Active", true, null, null, Now, []))
                : Result.Failure<CustomerProfile>(Error));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
