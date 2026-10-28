using System.Globalization;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;
using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

internal sealed class ParsianConfirmPaymentRequestFactory(ParsianOptions options)
{
    public Result<ConfirmPaymentRequest> Create(GatewayPaymentContext payment) =>
        long.TryParse(payment.Token, NumberStyles.None, CultureInfo.InvariantCulture, out var token) && token > 0
            ? Result.Success(new ConfirmPaymentRequest(options.LoginAccount, token))
            : Result.Failure<ConfirmPaymentRequest>(PaymentGatewayErrors.InvalidContext);
}
