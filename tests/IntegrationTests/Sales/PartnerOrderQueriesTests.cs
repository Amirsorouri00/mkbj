using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// What a partner may read, and — mostly — what it may not.
/// </summary>
/// <remarks>
/// Tenancy is the point of this class. Every query filters on the authenticated partner in SQL,
/// and another partner's order answers <b>NOT_FOUND</b> rather than FORBIDDEN: a 403 confirms
/// the id exists, which is exactly what someone probing for order numbers wants to learn.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class PartnerOrderQueriesTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PartnerPrincipal Principal(long partnerId) =>
        new(partnerId, 1, new HashSet<string>(StringComparer.Ordinal) { PartnerScopes.OrdersRead });

    [Fact]
    public async Task An_Order_Reads_Back_With_Its_Lines_And_Invoice()
    {
        using var factory = fixture.CreateFactory();
        var world = await SeedAsync(factory);

        var order = Value(await Queries(factory).GetAsync(Principal(world.PartnerId), world.OrderId, Ct));

        order.ExternalOrderId.Should().Be(world.ExternalOrderId);
        order.Currency.Should().Be("USD", "char(3) padding is trimmed on the way out");
        order.Lines.Should().ContainSingle();
        order.Status.Documents.Should().Be("Invoiced");

        order.Status.FulfillmentStatement.Should().Be(DocumentWording.FulfillmentStatement,
            "a partner reading this over the API makes the same assumption a reader of the "
            + "invoice would, and must be told the same thing");

        order.Invoice.Should().NotBeNull();
        order.CreditNotes.Should().BeEmpty();
    }

    [Fact]
    public async Task The_Invoice_Content_Is_The_Snapshot_That_Was_Issued()
    {
        using var factory = fixture.CreateFactory();
        var world = await SeedAsync(factory);

        var order = Value(await Queries(factory).GetAsync(Principal(world.PartnerId), world.OrderId, Ct));

        var content = order.Invoice!.Content.Deserialize<DocumentContent>(DocumentContentJson.Options)!;

        content.Title.Should().Be("Invoice");
        content.TaxStatement.Should().Be("Not GST registered");
        content.TotalMinor.Should().Be(order.TotalMinor);
    }

    [Fact]
    public async Task Another_Partners_Order_Is_Not_Found_Rather_Than_Forbidden()
    {
        using var factory = fixture.CreateFactory();
        var world = await SeedAsync(factory);
        var stranger = Principal(world.PartnerId + 1_000);
        var queries = Queries(factory);

        foreach (var error in new[]
        {
            (await queries.GetAsync(stranger, world.OrderId, Ct)).Error,
            (await queries.GetByExternalIdAsync(stranger, world.ExternalOrderId, Ct)).Error,
            (await queries.GetInvoiceAsync(stranger, world.InvoiceId, Ct)).Error,
            (await queries.ListForCustomerAsync(
                stranger, new CustomerKey.ById(world.CustomerId), null, 50, Ct)).Error,
        })
        {
            error.Should().NotBeNull();
            error!.Code.Should().Be("NOT_FOUND");
            error.HttpStatus.Should().Be(404, "403 would confirm the id exists");
        }
    }

    [Fact]
    public async Task Another_Partners_Order_Is_Absent_From_Lists()
    {
        using var factory = fixture.CreateFactory();
        var world = await SeedAsync(factory);

        var page = Value(await Queries(factory).ListAsync(
            Principal(world.PartnerId + 1_000), new PartnerOrderListQuery(
                null, null, null, null, null, null, null), Ct));

        page.Items.Should().NotContain(item => item.Id == world.OrderId);
    }

    [Fact]
    public async Task Listing_Pages_Newest_First_Without_Gaps_Or_Repeats()
    {
        using var factory = fixture.CreateFactory();
        var world = await SeedAsync(factory, orders: 3);
        var queries = Queries(factory);

        var first = Value(await queries.ListAsync(
            Principal(world.PartnerId),
            new PartnerOrderListQuery(null, null, null, null, null, null, null, Limit: 2), Ct));

        first.Items.Should().HaveCount(2);
        first.Items.Select(i => i.Id).Should().BeInDescendingOrder();
        first.NextCursor.Should().NotBeNull();

        var second = Value(await queries.ListAsync(
            Principal(world.PartnerId),
            new PartnerOrderListQuery(null, null, null, null, null, null, first.NextCursor, Limit: 2), Ct));

        second.Items.Should().ContainSingle();
        second.NextCursor.Should().BeNull();

        first.Items.Select(i => i.Id).Should().NotIntersectWith(second.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Every_Filter_Narrows()
    {
        using var factory = fixture.CreateFactory();
        var world = await SeedAsync(factory);
        var queries = Queries(factory);
        var principal = Principal(world.PartnerId);

        async Task<int> CountAsync(PartnerOrderListQuery query) =>
            Value(await queries.ListAsync(principal, query, Ct)).Items.Count;

        var none = new PartnerOrderListQuery(null, null, null, null, null, null, null);

        (await CountAsync(none with { ExternalOrderId = world.ExternalOrderId })).Should().Be(1);
        (await CountAsync(none with { ExternalOrderId = "nope" })).Should().Be(0);
        (await CountAsync(none with { CustomerId = world.CustomerId })).Should().Be(1);
        (await CountAsync(none with { ExternalCustomerId = "C-77" })).Should().Be(1);
        (await CountAsync(none with { ExternalCustomerId = "C-other" })).Should().Be(0);
        (await CountAsync(none with { Currency = "EUR" })).Should().Be(0);
        (await CountAsync(none with { From = DateTimeOffset.UtcNow.AddDays(1) })).Should().Be(0);
        (await CountAsync(none with { To = DateTimeOffset.UtcNow.AddDays(-1) })).Should().Be(0);
    }

    [Fact]
    public async Task A_Customer_With_No_Orders_For_This_Partner_Is_Not_Found()
    {
        using var factory = fixture.CreateFactory();
        var world = await SeedAsync(factory);

        var result = await Queries(factory).ListForCustomerAsync(
            Principal(world.PartnerId), new CustomerKey.ById(world.CustomerId + 999_999), null, 50, Ct);

        // Indistinguishable from a customer that does not exist, deliberately: anything else
        // lets a partner enumerate another's customers.
        result.Error!.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Searching_Invoices_Answers_For_Every_Id_In_Order()
    {
        using var factory = fixture.CreateFactory();
        var world = await SeedAsync(factory);

        var found = Value(await Queries(factory).SearchInvoicesAsync(
            Principal(world.PartnerId), ["missing-a", world.ExternalOrderId, "missing-b"], Ct));

        found.Select(item => item.ExternalOrderId)
            .Should().Equal("missing-a", world.ExternalOrderId, "missing-b");

        found[0].Found.Should().BeFalse();
        found[1].Found.Should().BeTrue();
        found[1].Invoices.Should().ContainSingle();
        found[2].Found.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Searching_Too_Few_Or_Too_Many_Ids_Is_Rejected(int count)
    {
        using var factory = fixture.CreateFactory();

        var result = await Queries(factory).SearchInvoicesAsync(
            Principal(1), [.. Enumerable.Range(0, count).Select(i => $"PO-{i}")], Ct);

        result.Error!.Code.Should().Be("VALIDATION_FAILED");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task An_Out_Of_Range_Limit_Is_Rejected(int limit)
    {
        using var factory = fixture.CreateFactory();

        var result = await Queries(factory).ListAsync(
            Principal(1),
            new PartnerOrderListQuery(null, null, null, null, null, null, null, limit), Ct);

        result.Error!.Code.Should().Be("VALIDATION_FAILED");
    }

    private sealed record SeededWorld(
        long PartnerId, long OrderId, long InvoiceId, long CustomerId, string ExternalOrderId);

    private static async Task<SeededWorld> SeedAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, int orders = 1)
    {
        var partnerId = SalesOrderMother.NextId();
        SeededWorld? first = null;

        for (var i = 0; i < orders; i++)
        {
            using var scope = factory.Services.CreateScope();
            var order = SalesOrderMother.PartnerOrder(partnerId, $"PO-{SalesOrderMother.NextId()}");

            (await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
                .TryInsertPartnerOrderAsync(order, Ct)).Should().BeTrue();

            first ??= new SeededWorld(
                partnerId, order.Id, order.Documents[0].Id, order.CustomerId, order.ExternalOrderId!);
        }

        return first!;
    }

    private static IPartnerOrderQueries Queries(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<IPartnerOrderQueries>();

    private static T Value<T>(EwaysVNext.Outcomes.Result<T> result)
    {
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }
}
