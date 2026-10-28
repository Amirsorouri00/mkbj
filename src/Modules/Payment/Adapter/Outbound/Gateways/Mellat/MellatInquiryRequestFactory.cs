using System.Globalization;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat.Contracts;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;

internal static class MellatInquiryRequestFactory
{
    public static Result<MellatInquiryRequest> Create(GatewayPaymentContext? payment)
    {
        if (payment is null
            || !string.Equals(payment.ProviderName, "mellat", StringComparison.OrdinalIgnoreCase)
            || !long.TryParse(payment.ProviderReference, NumberStyles.None, CultureInfo.InvariantCulture, out var reference)
            || reference <= 0)
        {
            return Result.Failure<MellatInquiryRequest>(PaymentGatewayErrors.InvalidContext);
        }

        return Result.Success(new MellatInquiryRequest(reference));
    }
}
