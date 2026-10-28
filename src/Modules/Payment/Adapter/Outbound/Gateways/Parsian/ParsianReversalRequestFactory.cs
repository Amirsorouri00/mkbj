using System.Globalization;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;
using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

internal sealed class ParsianReversalRequestFactory(ParsianOptions options)
{
    public Result<ReversalRequest> Create(GatewayPaymentContext payment) =>
        long.TryParse(payment.Token, NumberStyles.None, CultureInfo.InvariantCulture, out var token) && token > 0
            ? Result.Success(new ReversalRequest(options.LoginAccount, token))
            : Result.Failure<ReversalRequest>(PaymentGatewayErrors.InvalidContext);
}
