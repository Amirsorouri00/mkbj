using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Services;

/// <summary>
/// The partner order flow, and the order its steps must happen in.
/// </summary>
/// <remarks>
/// Every test here is about <b>when</b> something happens as much as whether it does. A
/// composition that fails must leave no customer, no order and no invoice, because the partner
/// will retry — and a half-made record would make that retry mean something different from what
/// the partner intended.
/// </remarks>
public sealed class PartnerOrderServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreatePartnerOrderCommand Command(long amountMinor = 50_000) => new(
        ExternalOrderId: "PO-1",
        ExternalCustomerId: "C-77",
        amountMinor,
        Currency: "USD",
        new PartnerContact("pat@example.com", "+6598765432", "Pat Lim"));

    [Fact]
    public async Task A_Retry_Returns_The_Original_Order_Without_Composing()
    {
        var world = new SalesWorld();
        world.Orders.Existing = new PartnerOrderKey(
            555, SalesWorld.FingerprintFor(Command()));

        var result = await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct);

        result.Value!.OrderId.Should().Be(555);
        result.Value.Replayed.Should().BeTrue();

        world.Pool.Calls.Should().Be(0, "a retry must be cheap");
        world.Customers.FindCalls.Should().Be(0);
        world.Attempts.Records.Should().BeEmpty("nothing was composed, so nothing was attempted");
    }

    [Fact]
    public async Task The_Same_Key_For_A_Different_Order_Is_A_Conflict()
    {
        var world = new SalesWorld();
        world.Orders.Existing = new PartnerOrderKey(555, new string('f', 64));

        var result = await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct);

        result.Error!.Code.Should().Be("ORDER_IDEMPOTENCY_CONFLICT");
        world.Pool.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(999L, false)]
    [InlineData(1_000L, true)]
    [InlineData(100_000_000L, true)]
    [InlineData(100_000_001L, false)]
    public async Task The_Amount_Is_Checked_Against_The_Seller_Limits(long usdMinor, bool allowed)
    {
        var world = new SalesWorld();
        world.Pricing.UsdOverride = usdMinor;

        var result = await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct);

        if (allowed)
        {
            result.IsSuccess.Should().BeTrue(result.Error?.Code);
        }
        else
        {
            result.Error!.Code.Should().Be("AMOUNT_OUT_OF_RANGE");
            world.Customers.FindCalls.Should().Be(0, "the cheapest refusal comes first");
        }
    }

    [Fact]
    public async Task A_New_Customer_Needs_Both_An_Email_And_A_Phone()
    {
        var world = new SalesWorld();
        world.Customers.Existing = null;

        var result = await world.Service.CreateAsync(
            world.Principal, Command() with { Contact = new PartnerContact("a@b.c", null, "Pat") },
            "corr", Ct);

        result.Error!.Code.Should().Be("CUSTOMER_CONTACT_REQUIRED");
        world.Customers.Created.Should().BeEmpty();
        world.Orders.Inserted.Should().BeEmpty();
    }

    [Fact]
    public async Task Creating_A_Customer_Needs_The_Write_Scope()
    {
        var world = new SalesWorld(PartnerScopes.OrdersCreate);
        world.Customers.Existing = null;

        var result = await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct);

        result.Error!.Code.Should().Be("INSUFFICIENT_SCOPE");
        world.Customers.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_An_Existing_Customer_Needs_The_Write_Scope()
    {
        var world = new SalesWorld(PartnerScopes.OrdersCreate);

        var result = await world.Service.CreateAsync(
            world.Principal,
            Command() with { Contact = new PartnerContact("changed@example.com", null, null) },
            "corr", Ct);

        result.Error!.Code.Should().Be("INSUFFICIENT_SCOPE");
    }

    [Fact]
    public async Task Sending_Unchanged_Details_Is_Not_A_Change()
    {
        var world = new SalesWorld(PartnerScopes.OrdersCreate);

        // The same details the record already holds. A partner that sends them on every order is
        // not asking to rewrite the record each time, so no write scope is needed.
        var result = await world.Service.CreateAsync(
            world.Principal,
            Command() with { Contact = new PartnerContact("stored@example.com", "+6500000000", "Stored Name") },
            "corr", Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Code);
        world.Customers.Synced.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_Buyer_Fields_Fail_Before_Any_Composition()
    {
        var world = new SalesWorld();
        world.Settings.RequiredBuyerFields = ["registrationNumber"];

        var result = await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct);

        result.Error!.Code.Should().Be("INVOICE_BUYER_FIELDS_MISSING");
        world.Attempts.Records.Should().BeEmpty();
        world.Orders.Inserted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Failed_Composition_Creates_No_Customer_And_Records_The_Attempt()
    {
        var world = new SalesWorld();
        world.Customers.Existing = null;
        world.Budgets.Expired = true;

        var result = await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct);

        result.Error!.Code.Should().Be("COMPOSITION_TIMEOUT");

        world.Customers.Created.Should().BeEmpty(
            "a partner that retries must not accumulate customers for orders that never existed");
        world.Orders.Inserted.Should().BeEmpty();

        var attempt = world.Attempts.Records.Should().ContainSingle().Which;
        attempt.Succeeded.Should().BeFalse();
        attempt.ErrorCode.Should().Be("COMPOSITION_TIMEOUT");
    }

    [Fact]
    public async Task A_New_Customer_Is_Created_After_The_Composition_And_Before_The_Insert()
    {
        var world = new SalesWorld();
        world.Customers.Existing = null;

        (await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct))
            .IsSuccess.Should().BeTrue();

        // "catalog:live" is the re-quote, which only happens once a basket exists — so its
        // position is where "composed" sits in the sequence.
        world.Journal.Should().ContainInOrder("catalog:live", "customer:create", "order:insert");
    }

    [Fact]
    public async Task Contact_Details_Sync_Only_After_The_Order_Is_Safe()
    {
        var world = new SalesWorld();

        (await world.Service.CreateAsync(
            world.Principal,
            Command() with { Contact = new PartnerContact("changed@example.com", null, null) },
            "corr", Ct)).IsSuccess.Should().BeTrue();

        world.Customers.Synced.Should().ContainSingle();
        world.Journal.Should().ContainInOrder("order:insert", "customer:sync");
    }

    [Fact]
    public async Task A_Retry_Never_Syncs_Contacts()
    {
        var world = new SalesWorld();
        world.Orders.Existing = new PartnerOrderKey(555, SalesWorld.FingerprintFor(Command()));

        await world.Service.CreateAsync(
            world.Principal,
            Command() with { Contact = new PartnerContact("changed@example.com", null, null) },
            "corr", Ct);

        world.Customers.Synced.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Failed_Sync_Still_Returns_A_Created_Order_With_A_Warning()
    {
        var world = new SalesWorld();
        world.Customers.SyncThrows = true;

        var result = await world.Service.CreateAsync(
            world.Principal,
            Command() with { Contact = new PartnerContact("changed@example.com", null, null) },
            "corr", Ct);

        result.IsSuccess.Should().BeTrue(
            "the order is sold, paid and invoiced; refusing it over a phone number would be "
            + "turning down money for bookkeeping");
        result.Value!.Warnings.Should().Equal("CONTACT_SYNC_FAILED");
    }

    [Fact]
    public async Task The_Invoice_Uses_The_Supplied_Contact_Over_The_Stored_One()
    {
        var world = new SalesWorld();

        await world.Service.CreateAsync(
            world.Principal,
            Command() with { Contact = new PartnerContact("new@example.com", null, null) },
            "corr", Ct);

        var order = world.Orders.Inserted.Should().ContainSingle().Which;

        order.Buyer.Email.Should().Be("new@example.com", "this order was placed with that address");
        order.Buyer.Phone.Should().Be("+6500000000", "and the rest falls back to what is stored");
    }

    [Fact]
    public async Task An_Unsellable_Variant_Is_Dropped_The_Pool_Invalidated_And_The_Basket_Recomposed()
    {
        var world = new SalesWorld();
        world.Catalog.WithdrawOnLiveRead = 1;

        var result = await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Code);

        world.Pool.Invalidations.Should().Contain("USD",
            "the cached pool is now known to be wrong, not merely old");

        world.Orders.Inserted.Single().Lines
            .Should().NotContain(line => line.VariantId == 1);
    }

    [Fact]
    public async Task A_Line_Records_The_Live_Reference_Price_Not_The_Cached_One()
    {
        var world = new SalesWorld();
        world.Pricing.LiveReferenceShift = 10;

        (await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct))
            .IsSuccess.Should().BeTrue();

        var line = world.Orders.Inserted.Single().Lines[0];

        // Both bands would have allowed the chosen price. The order must still say what the
        // catalogue actually said at the moment of sale.
        line.Price.ReferenceMinor.Should().Be(line.Price.BaseAmountMinor);
        line.Price.ReferenceMinor.Should().NotBe(SalesWorld.FakePricing.CachedReferenceFor(line.VariantId));
    }

    [Fact]
    public async Task A_Shop_That_Keeps_Moving_Gives_Up_Rather_Than_Looping()
    {
        var world = new SalesWorld();
        world.Catalog.WithdrawEverythingOnLiveRead = true;

        var result = await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct);

        result.Error!.Code.Should().Be("EXACT_COMPOSITION_UNAVAILABLE");
        world.Orders.Inserted.Should().BeEmpty();
    }

    [Fact]
    public async Task Losing_The_Insert_Race_Replays_The_Winner()
    {
        var world = new SalesWorld();
        world.Orders.RejectInsertThenReturn = new PartnerOrderKey(
            777, SalesWorld.FingerprintFor(Command()));

        var result = await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct);

        result.Value!.OrderId.Should().Be(777);
    }

    [Fact]
    public async Task Losing_The_Insert_Race_To_A_Different_Order_Is_A_Conflict()
    {
        var world = new SalesWorld();
        world.Orders.RejectInsertThenReturn = new PartnerOrderKey(777, new string('f', 64));

        var result = await world.Service.CreateAsync(world.Principal, Command(), "corr", Ct);

        result.Error!.Code.Should().Be("ORDER_IDEMPOTENCY_CONFLICT");
    }
}
