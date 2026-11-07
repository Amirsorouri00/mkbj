using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Carts;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Carts;

/// <summary>
/// The cart's own rules: quantities, ceilings, and what a reprice does to a line.
/// </summary>
/// <remarks>
/// The cart holds the price the customer was <b>shown</b>, never an agreed one. Everything here
/// is about keeping that figure honest — refreshed when they look again, dropped when nobody
/// will honour it — so that checkout has something real to compare a live quote against.
/// </remarks>
public sealed class CartTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void A_Quantity_Outside_One_To_999_Is_Refused(int quantity)
    {
        var act = () => NewCart().AddOrIncrement(1, 10, quantity, 500, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public void An_Increment_Past_999_Is_Refused_Rather_Than_Clamped()
    {
        var cart = NewCart();
        cart.AddOrIncrement(1, 10, 998, 500, Now);

        var act = () => cart.AddOrIncrement(1, 10, 2, 500, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("VALIDATION_FAILED");
        cart.Lines.Single().Quantity.Should().Be(998, "a refused change leaves the cart alone");
    }

    [Fact]
    public void The_Hundred_And_First_Line_Is_Refused()
    {
        var cart = NewCart();
        for (var i = 0; i < Cart.MaxLines; i++) cart.AddOrIncrement(i, 100 + i, 1, 500, Now);

        var act = () => cart.AddOrIncrement(999, 9_999, 1, 500, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public void Adding_An_Existing_Variant_Adds_Quantity_And_Refreshes_The_Shown_Price()
    {
        var cart = NewCart();
        cart.AddOrIncrement(1, 10, 2, 500, Now);

        var later = Now.AddHours(1);
        cart.AddOrIncrement(2, 10, 3, 600, later);

        var line = cart.Lines.Should().ContainSingle().Subject;
        line.Quantity.Should().Be(5);
        line.DisplayedUnitPriceMinor.Should().Be(600,
            "the customer is looking at today's price for the whole line, not a blend of visits");
        line.PriceQuotedAt.Should().Be(later);
        cart.TotalMinor.Should().Be(3_000);
    }

    [Fact]
    public void Repricing_Updates_What_It_Can_And_Drops_What_It_Cannot()
    {
        var cart = NewCart();
        cart.AddOrIncrement(1, 10, 2, 500, Now);
        cart.AddOrIncrement(2, 20, 1, 800, Now);

        var later = Now.AddDays(1);
        cart.Reprice("EUR", new Dictionary<long, long> { [10] = 450 }, later);

        cart.Currency.Should().Be("EUR");
        cart.Lines.Select(line => line.VariantId).Should().Equal(10);
        cart.Lines.Single().DisplayedUnitPriceMinor.Should().Be(450);
        cart.TotalMinor.Should().Be(900);
        cart.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void Clearing_Empties_The_Cart()
    {
        var cart = NewCart();
        cart.AddOrIncrement(1, 10, 2, 500, Now);

        cart.Clear(Now);

        cart.Lines.Should().BeEmpty();
        cart.TotalMinor.Should().Be(0);
    }

    [Fact]
    public void Removing_A_Variant_That_Is_Not_There_Is_Not_An_Error()
    {
        var cart = NewCart();
        cart.AddOrIncrement(1, 10, 2, 500, Now);

        // Idempotent on purpose: a customer clicking remove twice has said one thing.
        cart.Remove(99, Now);
        cart.Remove(10, Now);
        cart.Remove(10, Now);

        cart.Lines.Should().BeEmpty();
    }

    private static Cart NewCart() => Cart.Create(1, 42, "USD", Now);
}
