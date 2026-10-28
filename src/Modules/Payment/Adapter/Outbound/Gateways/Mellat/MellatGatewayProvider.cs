using System.Globalization;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat.Contracts;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Options;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;

internal sealed class MellatGatewayProvider(
    MellatHttpClient http,
    MellatInquiryHttpClient inquiryHttp,
    IOptions<MellatOptions> options,
    IOptions<MellatInquiryOptions> inquiryOptions,
    TimeProvider timeProvider) : IPaymentGatewayProvider, IPaymentReversalProvider, IPaymentInquiryProvider
{
    public string Name => "mellat";

    public async Task<Result<GatewayLink>> CreateLinkAsync(GatewayLinkRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var saleRequest = new MellatSaleRequestFactory(options.Value, timeProvider).Create(request);
        if (saleRequest.IsFailure) return Result.Failure<GatewayLink>(saleRequest.Error!);

        var result = await http.PostAsync<MellatSaleRequest, MellatResponse>("bpPayRequest", saleRequest.Value!, false, ct);
        if (result.IsFailure) return Result.Failure<GatewayLink>(result.Error!);
        var response = result.Value!;
        if (string.IsNullOrWhiteSpace(response.ResCode))
            return Result.Failure<GatewayLink>(PaymentGatewayErrors.InvalidResponse);
        if (!string.Equals(response.ResCode, "0", StringComparison.Ordinal))
            return Result.Failure<GatewayLink>(MellatStatus.Refusal(response.ResCode));
        if (string.IsNullOrEmpty(response.EncRefId) || response.InternalRefrenceId is not > 0)
            return Result.Failure<GatewayLink>(PaymentGatewayErrors.InvalidResponse);

        return Result.Success(new GatewayLink(response.EncRefId, new GatewayRedirect(
            new Uri(options.Value.PaymentPageUrl), GatewayRedirectMethod.Post,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["RefId"] = response.EncRefId })));
    }

    public async Task<Result<GatewayCallbackResult>> VerifyCallbackAsync(GatewayCallbackRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request?.Payment is null || request.Fields is null || !IsOwnContext(request.Payment))
            return Result.Failure<GatewayCallbackResult>(PaymentGatewayErrors.InvalidContext);

        // Documented checks: RefId and SaleOrderId must match the attempt; never Verify a mismatched callback.
        var payment = request.Payment;
        var form = MellatCallbackFormPayload.Parse(request.Fields);
        if (form is null
            || !string.Equals(form.RefId, payment.Token, StringComparison.Ordinal)
            || form.SaleOrderId != payment.RequestId
            || (form.ResCode == "0" && form.SaleReferenceId is null))
        {
            return Result.Failure<GatewayCallbackResult>(PaymentGatewayErrors.InvalidCallback);
        }

        var reference = form.SaleReferenceId?.ToString(CultureInfo.InvariantCulture);
        if (form.ResCode != "0")
            return Outcome(payment, RefusalState(form.ResCode), reference, form.ResCode);

        return await VerifyAsync(payment, form.SaleReferenceId!.Value, ct);
    }

    // The callback alone proves nothing; success requires bpVerifyRequest.
    private async Task<Result<GatewayCallbackResult>> VerifyAsync(GatewayPaymentContext payment, long saleReferenceId, CancellationToken ct)
    {
        var reference = saleReferenceId.ToString(CultureInfo.InvariantCulture);
        var result = await SendAsync("bpVerifyRequest", payment, saleReferenceId, ct);
        // Transport failure after the retry, HTTP errors and malformed replies leave the outcome unknown.
        if (result.IsFailure || string.IsNullOrWhiteSpace(result.Value!.ResCode))
            return Outcome(payment, GatewayPaymentState.Unknown, reference, null);

        var code = result.Value.ResCode;
        var state = code switch
        {
            "0" or "43" or "45" => GatewayPaymentState.Confirmed,
            "48" => GatewayPaymentState.Reversed,
            _ => RefusalState(code)
        };
        return Outcome(payment, state, reference, code);
    }

    public async Task<Result<GatewayReversalResult>> ReverseAsync(GatewayPaymentContext payment, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Mellat identifies a paid sale by the SaleReferenceId retained from the confirmed callback.
        if (payment is null || !IsOwnContext(payment)
            || !long.TryParse(payment.ProviderReference, NumberStyles.None, CultureInfo.InvariantCulture, out var reference)
            || reference <= 0)
        {
            return Result.Failure<GatewayReversalResult>(PaymentGatewayErrors.InvalidContext);
        }

        var result = await SendAsync("bpReversalRequest", payment, reference, ct);
        if (result.IsFailure) return Result.Failure<GatewayReversalResult>(result.Error!);
        var code = result.Value!.ResCode;
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<GatewayReversalResult>(PaymentGatewayErrors.InvalidResponse);

        GatewayReversalState? state = code switch
        {
            "0" or "48" => GatewayReversalState.Reversed,
            "43" or "45" => GatewayReversalState.StillConfirmed,
            _ => RefusalState(code) == GatewayPaymentState.Unknown ? GatewayReversalState.Unknown : null
        };
        if (state is { } observed)
        {
            return Result.Success(new GatewayReversalResult(
                observed, reference.ToString(CultureInfo.InvariantCulture), code, null));
        }
        return Result.Failure<GatewayReversalResult>(MellatStatus.Refusal(code));
    }

    public async Task<Result<GatewayInquiryObservation>> InquiryAsync(GatewayPaymentContext payment, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!inquiryOptions.Value.Enabled)
            return Result.Failure<GatewayInquiryObservation>(MellatInquiryErrors.NotConfigured);

        var request = MellatInquiryRequestFactory.Create(payment);
        if (request.IsFailure)
            return Result.Failure<GatewayInquiryObservation>(request.Error!);

        var result = await inquiryHttp.GetByReferenceAsync(request.Value!, ct);
        if (result.IsFailure)
            return Result.Failure<GatewayInquiryObservation>(result.Error!);

        var response = result.Value!;
        if (string.IsNullOrWhiteSpace(response.ResponseCode))
            return Result.Failure<GatewayInquiryObservation>(PaymentGatewayErrors.InvalidResponse);
        if (string.Equals(response.ResponseCode, "914", StringComparison.Ordinal))
            return Result.Failure<GatewayInquiryObservation>(PaymentGatewayErrors.TransactionNotFound);
        if (!string.Equals(response.ResponseCode, "000", StringComparison.Ordinal))
            return Result.Failure<GatewayInquiryObservation>(
                MellatInquiryErrors.Refusal(response.ResponseCode, response.ResponseMessage));
        if (response.Success is not true)
            return Result.Failure<GatewayInquiryObservation>(PaymentGatewayErrors.InvalidResponse);
        if (response.TransactionInfoList is not { Count: 1 })
            return Result.Failure<GatewayInquiryObservation>(PaymentGatewayErrors.InvalidResponse);

        var record = response.TransactionInfoList[0];
        var requestedReference = request.Value!.ReferenceId;
        if (record.InternalReferenceId != requestedReference
            || (record.TerminalId is { } terminal && terminal != options.Value.TerminalId)
            || record.Amount is not > 0)
        {
            return Result.Failure<GatewayInquiryObservation>(PaymentGatewayErrors.InvalidResponse);
        }

        return Result.Success(new GatewayInquiryObservation(
            Money.Create(record.Amount.Value, Money.RialCurrency),
            payment.ProviderReference!,
            ParseTransactionTime(record.TransactionDate, record.TransactionTime),
            ProviderCode: response.ResponseCode,
            ProviderMessage: response.ResponseMessage));
    }

    private Task<Result<MellatResponse>> SendAsync(string path, GatewayPaymentContext payment, long saleReferenceId, CancellationToken ct)
    {
        // The retained RequestId is sent as both orderId and saleOrderId, which Mellat permits.
        var request = new MellatTransactionRequest(
            options.Value.TerminalId,
            options.Value.UserName,
            options.Value.UserPassword,
            payment.RequestId,
            payment.RequestId,
            saleReferenceId);
        return http.PostAsync<MellatTransactionRequest, MellatResponse>(path, request, true, ct);
    }

    // Documented refusals are final; uncertain and undocumented codes stay unknown.
    private static GatewayPaymentState RefusalState(string code) =>
        MellatStatus.IsDocumented(code) && !MellatStatus.IsUncertain(code) ? GatewayPaymentState.Failed : GatewayPaymentState.Unknown;

    private static Result<GatewayCallbackResult> Outcome(GatewayPaymentContext payment, GatewayPaymentState state, string? reference, string? code) =>
        Result.Success(new GatewayCallbackResult(payment.RequestId, payment.Token, reference, state, code));

    private bool IsOwnContext(GatewayPaymentContext payment) =>
        string.Equals(payment.ProviderName, Name, StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ParseTransactionTime(string? date, string? time)
    {
        if (!DateTime.TryParseExact(date + " " + time, "yyyy/MM/dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var zone = TehranTimeZone();
        if (zone.IsInvalidTime(local))
            return null;
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo TehranTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"); }
    }
}
