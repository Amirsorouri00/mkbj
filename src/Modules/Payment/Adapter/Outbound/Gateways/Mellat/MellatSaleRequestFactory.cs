using System.Globalization;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat.Contracts;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;

internal sealed class MellatSaleRequestFactory(MellatOptions options, TimeProvider timeProvider)
    : GatewaySaleRequestFactoryBase<MellatSaleRequest>
{
    private static readonly TimeZoneInfo TehranTimeZone = ResolveTehranTimeZone();

    protected override Result<MellatSaleRequest> CreateProviderRequest(GatewayLinkRequest request)
    {
        if (!string.Equals(request.Amount.Currency, Money.RialCurrency, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<MellatSaleRequest>(PaymentGatewayErrors.UnsupportedCurrency);
        if (request.Amount.Amount < 1000m || request.Amount.Amount > long.MaxValue
            || decimal.Truncate(request.Amount.Amount) != request.Amount.Amount)
            return Result.Failure<MellatSaleRequest>(PaymentGatewayErrors.UnsupportedAmount);
        if (!MellatOptions.IsHttpsAddress(request.CallbackUrl.AbsoluteUri))
            return Result.Failure<MellatSaleRequest>(PaymentGatewayErrors.UnsupportedCallback);

        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TehranTimeZone);
        return Result.Success(new MellatSaleRequest(
            options.TerminalId,
            options.UserName,
            options.UserPassword,
            request.RequestId,
            decimal.ToInt64(request.Amount.Amount).ToString(CultureInfo.InvariantCulture),
            local.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            local.ToString("HHmmss", CultureInfo.InvariantCulture),
            string.Empty,
            request.CallbackUrl.AbsoluteUri,
            "0"));
    }

    private static TimeZoneInfo ResolveTehranTimeZone()
    {
        foreach (var id in new[] { "Asia/Tehran", "Iran Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        throw new TimeZoneNotFoundException("The Tehran time zone is unavailable.");
    }
}
