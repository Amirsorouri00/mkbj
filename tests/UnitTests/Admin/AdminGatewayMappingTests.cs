using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Modules.Admin.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Queries.Application.Paginations;
using EwaysVNext.Modules.Customer.QueryApplication.Customers;
using EwaysVNext.Modules.Sales.QueryApplication.Admin;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using EwaysVNext.Outcomes;
using Xunit;

namespace EwaysVNext.UnitTests.Admin;

/// <summary>
/// The hand-written mappings, against the real provider types.
/// </summary>
/// <remarks>
/// This is the one place a field-by-field mapping can be shown to be right. The mapping exists so
/// a rename in Sales is a compile error in one file rather than a silent change to the backoffice
/// schema — but a mapping that quietly drops a field compiles perfectly, and only a test that
/// names the fields catches it.
/// </remarks>
public sealed class AdminGatewayMappingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_Sales_Dashboard_Carries_Every_Number_Across()
    {
        var snapshot = await new SalesReadGateway(new FakeSales()).GetDashboardAsync(Now, Ct);

        snapshot.OrderTotals.Should().Equal(
            new EwaysVNext.Modules.Admin.Application.Views.OrderTotalsRow(
                "24h", "PartnerApi", "USD", 3, 30_000));

        snapshot.PendingWebsitePayments.Count.Should().Be(1);
        snapshot.PendingWebsitePayments.AmountsByCurrency.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Currency = "EUR", AmountMinor = 5_000L });
        snapshot.PendingWebsitePayments.OldestCreatedAt.Should().Be(Now.AddHours(-5));

        snapshot.Composition24h.SuccessRate.Should().Be(0.8000m);
        snapshot.Composition24h.P95DurationMs.Should().Be(760);
        snapshot.Composition7d.Attempts.Should().Be(11);

        snapshot.RecentFailures.Should().ContainSingle()
            .Which.ErrorCode.Should().Be("COMPOSITION_TIMEOUT");
    }

    [Fact]
    public async Task An_Order_Sheet_Counts_Lines_And_Builds_Admin_Pdf_Paths()
    {
        var sheet = (await new SalesReadGateway(new FakeSales()).GetOrderAsync(42, Ct)).Value!;

        sheet.Summary.Id.Should().Be(42);
        sheet.Summary.Status.Documents.Should().Be("Invoiced");
        sheet.CorrelationId.Should().Be("corr-1");

        // The lines themselves stay on Sales' own detail route; the sheet is what an operator
        // scans.
        sheet.LineCount.Should().Be(3);

        sheet.Documents.Select(document => document.PdfPath).Should().Equal(
            "/api/v1/admin/documents/7/pdf", "/api/v1/admin/documents/8/pdf");

        sheet.Documents[1].RelatedDocumentId.Should().Be(7, "a credit note points at its invoice");
        sheet.Payments.Should().ContainSingle().Which.Method.Should().Be("bank-transfer");
        sheet.Refunds.Should().ContainSingle().Which.AmountMinor.Should().Be(1_000);
    }

    [Fact]
    public async Task A_Providers_Refusal_Travels_Unchanged()
    {
        var gateway = new SalesReadGateway(new FakeSales { NotFound = true });

        var result = await gateway.GetOrderAsync(404, Ct);

        // Admin has nothing to add to "no such order", and translating it would only risk
        // turning a 404 into a 500.
        result.Error!.HttpStatus.Should().Be(404);
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task A_Customer_Profile_Keeps_Its_Address_And_Orders_History_Newest_First()
    {
        var profile = (await new CustomerReadGateway(new FakeCustomers()).GetAsync(7, Ct)).Value!;

        profile.Id.Should().Be(7);
        profile.Kind.Should().Be("Website");
        profile.EmailVerified.Should().BeTrue();
        profile.RegistrationNumber.Should().Be("UEN-9");
        profile.BillingAddress.Should().Be(
            new EwaysVNext.Modules.Admin.Application.Views.AddressView(
                "1 Example Road", "Unit 2", "Singapore", "049999", "SG"));

        // An operator reading contact history is asking what changed most recently.
        profile.ContactHistory.Select(change => change.Field).Should().Equal("phone", "email");
    }

    private sealed class FakeSales : ISalesAdminQueries
    {
        public bool NotFound { get; init; }

        public Task<SalesDashboardDto> GetDashboardAsync(
            DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(new SalesDashboardDto(
                [new ChannelCurrencyTotalsDto("24h", "PartnerApi", "USD", 3, 30_000)],
                new PendingWebsitePaymentsDto(
                    1, [new CurrencyAmountDto("EUR", 5_000)], now.AddHours(-5)),
                new CompositionStatsDto("24h", 10, 8, 0.8000m, 760),
                new CompositionStatsDto("7d", 11, 9, 0.8182m, 800),
                [new CompositionFailureDto(now, "COMPOSITION_TIMEOUT", "USD", 100_000, 12, 7)]));

        public Task<Result<CursorPage<AdminOrderListItemDto>>> ListOrdersAsync(
            AdminOrderFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<AdminOrderDetailDto>> GetOrderAsync(
            long orderId, CancellationToken ct = default) =>
            Task.FromResult(NotFound
                ? Result.Failure<AdminOrderDetailDto>(new Error("NOT_FOUND", "No such order.", 404))
                : Result.Success(new AdminOrderDetailDto(
                    Summary(orderId),
                    "C-77",
                    4_000,
                    "corr-1",
                    JsonSerializer.SerializeToElement(new { }),
                    [Line(1), Line(2), Line(3)],
                    [new AdminPaymentDto(
                        9, "AdminConfirmed", "bank-transfer", "TT-1", Guid.Empty, Now, 4_000)],
                    [
                        new DocumentDto(7, "Commercial", "INV-000001", Now, null, Empty()),
                        new DocumentDto(8, "CreditNote", "CN-000001", Now, 7, Empty()),
                    ],
                    [new AdminRefundDto(5, 1_000, "Damaged", Guid.Empty, Now, "RTN-1", 8)],
                    null)));

        public Task<Result<DocumentDto>> GetDocumentAsync(
            long documentId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AdminOrderListItemDto>> ListCustomerOrdersAsync(
            long customerId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdminOrderListItemDto>>([Summary(1)]);

        private static AdminOrderListItemDto Summary(long id) => new(
            id, "ORD-000001", "Website", null, null, 7, "USD", 4_000, 1_000,
            new OrderStatusDto(
                "Completed", "PartiallyRefunded", "AdminConfirmed", "Fulfilled",
                "SystemAutomatic", "statement", "Invoiced"),
            Now);

        private static OrderLineDto Line(int lineNo) => new(
            lineNo, 10, 100, "SKU", "Title", "Brand", [], new Dictionary<string, string>(),
            1, 1_000, 1_000, 1_000, 0, "Base");

        private static JsonElement Empty() => JsonSerializer.SerializeToElement(new { });
    }

    private sealed class FakeCustomers : ICustomerAdminQueries
    {
        public Task<PaginatedQuery<CustomerAdminListItem>> ListAsync(
            AdminCustomerListFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<CustomerAdminDetail>> GetAsync(
            long customerId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new CustomerAdminDetail(
                new CustomerAdminListItem(
                    customerId, "Website", null, null, "a@example.test", "+6580000000",
                    "Pat Lim", "Lim Trading", "Active", Now.AddDays(-30)),
                EmailVerified: true,
                new AddressSnapshot("1 Example Road", "Unit 2", "Singapore", "049999", "SG"),
                "UEN-9",
                Guid.Empty,
                [
                    new ContactChangeView("email", "old@x", "a@example.test", Now.AddDays(-10), null, "c1"),
                    new ContactChangeView("phone", null, "+6580000000", Now.AddDays(-1), 7, "c2"),
                ])));
    }
}
