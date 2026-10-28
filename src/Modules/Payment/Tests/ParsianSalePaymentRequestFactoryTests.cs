using System.Globalization;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using Xunit;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.GatewayTests;

// Unsupported currency is not covered: Money refuses every non-IRR currency today, so no such request can be built.
public sealed class ParsianSalePaymentRequestFactoryTests
{
    private static readonly ParsianSalePaymentRequestFactory Factory = new(new ParsianOptions { LoginAccount = "test-only-credential" });

    [Fact]
    public void Valid_irr_request_maps_to_sale_payment_request()
    {
        var result = Factory.Create(Request());

        Assert.True(result.IsSuccess);
        var sale = result.Value!;
        Assert.Equal("test-only-credential", sale.LoginAccount);
        Assert.Equal(1001, sale.OrderId);
        Assert.Equal(1000L, sale.Amount);
        Assert.Equal("https://merchant.test/callback", sale.CallBackUrl);
        Assert.Null(sale.AdditionalData);
        Assert.Null(sale.Originator);
    }

    [Theory]
    [InlineData("1000")]
    [InlineData("9223372036854775807")]
    public void Boundary_amounts_are_accepted_without_rounding(string amount)
    {
        var result = Factory.Create(Request() with { Amount = Money.Create(Parse(amount)) });

        Assert.True(result.IsSuccess);
        Assert.Equal(long.Parse(amount, CultureInfo.InvariantCulture), result.Value!.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_request_id_is_invalid(long requestId)
    {
        Assert.Equal(PaymentGatewayErrors.InvalidRequest, Factory.Create(Request() with { RequestId = requestId }).Error);
    }

    [Fact]
    public void Incomplete_requests_are_invalid()
    {
        Assert.Equal(PaymentGatewayErrors.InvalidRequest, Factory.Create(null!).Error);
        Assert.Equal(PaymentGatewayErrors.InvalidRequest, Factory.Create(Request() with { BankTransactionId = 0 }).Error);
        Assert.Equal(PaymentGatewayErrors.InvalidRequest, Factory.Create(Request() with { Amount = null! }).Error);
        Assert.Equal(PaymentGatewayErrors.InvalidRequest, Factory.Create(Request() with { CallbackUrl = new Uri("/callback", UriKind.Relative) }).Error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("999.99")]
    public void Amount_below_parsian_minimum_is_unsupported(string amount)
    {
        Assert.Equal(PaymentGatewayErrors.UnsupportedAmount, Factory.Create(Request() with { Amount = Money.Create(Parse(amount)) }).Error);
    }

    [Fact]
    public void Fractional_amount_is_unsupported()
    {
        Assert.Equal(PaymentGatewayErrors.UnsupportedAmount, Factory.Create(Request() with { Amount = Money.Create(1000.50m) }).Error);
    }

    [Fact]
    public void Amount_beyond_int64_is_unsupported()
    {
        Assert.Equal(PaymentGatewayErrors.UnsupportedAmount, Factory.Create(Request() with { Amount = Money.Create((decimal)long.MaxValue + 1) }).Error);
    }

    [Theory]
    [InlineData("http://merchant.test/callback")]
    [InlineData("https://user:pw@merchant.test/callback")]
    [InlineData("https://merchant.test/callback#fragment")]
    public void Callback_outside_parsian_restrictions_is_unsupported(string callback)
    {
        Assert.Equal(PaymentGatewayErrors.UnsupportedCallback, Factory.Create(Request() with { CallbackUrl = new Uri(callback) }).Error);
    }

    [Fact]
    public void Amount_is_checked_before_callback()
    {
        var request = Request() with { Amount = Money.Create(1000.50m), CallbackUrl = new Uri("http://merchant.test/callback") };

        Assert.Equal(PaymentGatewayErrors.UnsupportedAmount, Factory.Create(request).Error);
    }

    private static GatewayLinkRequest Request() => new(1001, 42, Money.Create(1000), new Uri("https://merchant.test/callback"));

    private static decimal Parse(string amount) => decimal.Parse(amount, CultureInfo.InvariantCulture);
}
