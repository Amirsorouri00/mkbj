using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

internal sealed class ParsianSalePaymentRequestFactory(ParsianOptions options)
    : GatewaySaleRequestFactoryBase<SalePaymentRequest>
{
    private const decimal MinimumAmount = 1000m;

    protected override Result<SalePaymentRequest> CreateProviderRequest(GatewayLinkRequest request)
    {
        if (!string.Equals(request.Amount.Currency, Money.RialCurrency, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<SalePaymentRequest>(PaymentGatewayErrors.UnsupportedCurrency);

        var amount = request.Amount.Amount;
        if (amount < MinimumAmount || amount > long.MaxValue || decimal.Truncate(amount) != amount)
            return Result.Failure<SalePaymentRequest>(PaymentGatewayErrors.UnsupportedAmount);

        var callback = request.CallbackUrl;
        if (callback.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(callback.UserInfo) || !string.IsNullOrEmpty(callback.Fragment))
            return Result.Failure<SalePaymentRequest>(PaymentGatewayErrors.UnsupportedCallback);

        return Result.Success(new SalePaymentRequest(
            options.LoginAccount, request.RequestId, (long)amount, callback.AbsoluteUri));
    }
}
