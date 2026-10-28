using System.Globalization;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

internal static class ParsianSaleReportRequestFactory
{
    public static Result<SaleReportRequest> Create(GatewayPaymentContext? payment)
    {
        if (payment is null
            || !string.Equals(payment.ProviderName, "parsian", StringComparison.OrdinalIgnoreCase)
            || !long.TryParse(payment.ProviderReference, NumberStyles.None, CultureInfo.InvariantCulture, out var rrn)
            || rrn <= 0
            || !long.TryParse(payment.Token, NumberStyles.None, CultureInfo.InvariantCulture, out var token)
            || token <= 0
            || payment.RequestId <= 0
            || payment.Attempt.RequestedAt == default)
        {
            return Result.Failure<SaleReportRequest>(PaymentGatewayErrors.InvalidContext);
        }

        var local = TimeZoneInfo.ConvertTime(payment.Attempt.RequestedAt, TehranTimeZone());
        var from = DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified);
        var to = from.AddDays(1).AddTicks(-1);
        return Result.Success(new SaleReportRequest(rrn, payment.RequestId, token, from, to));
    }

    private static TimeZoneInfo TehranTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"); }
    }
}
