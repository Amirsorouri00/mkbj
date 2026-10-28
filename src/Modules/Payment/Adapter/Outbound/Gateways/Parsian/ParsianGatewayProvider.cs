using System.Globalization;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Options;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

internal sealed class ParsianGatewayProvider(
    ParsianHttpClient http,
    ParsianSaleReportHttpClient reportHttp,
    IOptions<ParsianOptions> options)
    : IPaymentGatewayProvider, IPaymentReversalProvider, IPaymentInquiryProvider
{
    public string Name => "parsian";

    public async Task<Result<GatewayLink>> CreateLinkAsync(GatewayLinkRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var saleRequest = new ParsianSalePaymentRequestFactory(options.Value).Create(request);
        if (saleRequest.IsFailure) return Result.Failure<GatewayLink>(saleRequest.Error!);

        var result = await http.PostAsync<SalePaymentRequest, SalePaymentResponse>("Payment/SalePaymentRequest",
            saleRequest.Value!, false, ct);
        if (result.IsFailure) return Result.Failure<GatewayLink>(result.Error!);
        var response = result.Value!;
        if (response.Status is not { } status) return Result.Failure<GatewayLink>(PaymentGatewayErrors.InvalidResponse);
        if (status != 0) return Result.Failure<GatewayLink>(ParsianStatus.Refusal(status, response.Message));
        if (response.Token is not > 0) return Result.Failure<GatewayLink>(PaymentGatewayErrors.InvalidResponse);

        var token = response.Token.Value.ToString(CultureInfo.InvariantCulture);
        return Result.Success(new GatewayLink(token, new GatewayRedirect(
            new Uri(options.Value.PaymentPageUrl + "?token=" + token), GatewayRedirectMethod.Get,
            new Dictionary<string, string>())));
    }

    public async Task<Result<GatewayCallbackResult>> VerifyCallbackAsync(GatewayCallbackRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request?.Payment is null || request.Fields is null || !IsOwnContext(request.Payment))
            return Result.Failure<GatewayCallbackResult>(PaymentGatewayErrors.InvalidContext);

        // Only status and RRN are documented; Token/OrderId are not, so they are not read (confirmation proves the payment).
        var form = CallbackFormPayload.Parse(request.Fields);
        if (form is null || (form.Status == 0 && form.Rrn is null))
            return Result.Failure<GatewayCallbackResult>(PaymentGatewayErrors.InvalidCallback);

        var payment = request.Payment;
        if (form.Status != 0)
            return Outcome(payment, form.Status == -138 ? GatewayPaymentState.Failed : GatewayPaymentState.Unknown, null, form.RawStatus);

        return await ConfirmAsync(payment, form.Rrn!.Value, ct);
    }

    // The callback alone proves nothing; success requires the bank's server-side confirmation.
    private async Task<Result<GatewayCallbackResult>> ConfirmAsync(GatewayPaymentContext payment, long callbackRrn, CancellationToken ct)
    {
        var request = new ParsianConfirmPaymentRequestFactory(options.Value).Create(payment);
        if (request.IsFailure) return Result.Failure<GatewayCallbackResult>(request.Error!);

        var paidReference = callbackRrn.ToString(CultureInfo.InvariantCulture);
        var result = await http.PostAsync<ConfirmPaymentRequest, ConfirmPaymentResponse>("payment/ConfirmPayment",
            request.Value!, true, ct);
        // Transport failure after the retry, HTTP errors and malformed replies leave the outcome unknown.
        if (result.IsFailure || result.Value!.Status is not { } status)
            return Outcome(payment, GatewayPaymentState.Unknown, paidReference, null);

        var response = result.Value;
        var code = ParsianStatus.Code(status);
        if (status != 0)
            return Outcome(payment, ParsianStatus.IsKnownRefusal(status) ? GatewayPaymentState.Failed : GatewayPaymentState.Unknown, paidReference, code);
        // The callback keeps the RRN as the bank sent it (e.g. "000456"); both are compared as numbers.
        if (response.Token != request.Value!.Token || response.RRN != callbackRrn)
            return Outcome(payment, GatewayPaymentState.Unknown, paidReference, code);

        return Outcome(payment, GatewayPaymentState.Confirmed, paidReference, code);
    }

    public async Task<Result<GatewayReversalResult>> ReverseAsync(GatewayPaymentContext payment, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsOwnContext(payment))
            return Result.Failure<GatewayReversalResult>(PaymentGatewayErrors.InvalidContext);

        var request = new ParsianReversalRequestFactory(options.Value).Create(payment);
        if (request.IsFailure) return Result.Failure<GatewayReversalResult>(request.Error!);

        var result = await http.PostAsync<ReversalRequest, ReversalResponse>("Payment/ReversalRequest",
            request.Value!, true, ct);
        if (result.IsFailure) return Result.Failure<GatewayReversalResult>(result.Error!);
        var response = result.Value!;
        if (response.Status is not { } status) return Result.Failure<GatewayReversalResult>(PaymentGatewayErrors.InvalidResponse);
        if (status != 0) return NonSuccess(status, response.Message);
        if (response.Token != request.Value!.Token) return Result.Failure<GatewayReversalResult>(PaymentGatewayErrors.InvalidResponse);
        return Result.Success(new GatewayReversalResult(GatewayReversalState.Reversed, ProviderCode: ParsianStatus.Code(status), ProviderMessage: response.Message));
    }

    public async Task<Result<GatewayInquiryObservation>> InquiryAsync(GatewayPaymentContext payment, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var request = ParsianSaleReportRequestFactory.Create(payment);
        if (request.IsFailure)
            return Result.Failure<GatewayInquiryObservation>(request.Error!);

        var saleRequest = request.Value!;
        var result = await reportHttp.GetSaleReportAsync(saleRequest, ct);
        if (result.IsFailure)
            return Result.Failure<GatewayInquiryObservation>(result.Error!);

        var response = result.Value!;
        if (response.Status != 0)
            return Result.Failure<GatewayInquiryObservation>(ParsianStatus.ReportFailure(response.Status, response.Message));
        if (response.Records.Count == 0)
            return Result.Failure<GatewayInquiryObservation>(PaymentGatewayErrors.TransactionNotFound);

        var matches = response.Records.Where(record =>
            record.RRN == saleRequest.RRN
            && record.OrderId == saleRequest.OrderId
            && record.Token == saleRequest.Token).ToArray();
        if (matches.Length != 1 || matches[0].Amount <= 0)
            return Result.Failure<GatewayInquiryObservation>(PaymentGatewayErrors.InvalidResponse);

        var record = matches[0];
        return Result.Success(new GatewayInquiryObservation(
            Money.Create(record.Amount, Money.RialCurrency),
            payment.ProviderReference!,
            ToTehranOffset(record.RequestDateTime),
            record.PGWStatusId?.ToString(CultureInfo.InvariantCulture),
            ParsianStatus.Code(response.Status),
            response.Message));
    }

    private static Result<GatewayCallbackResult> Outcome(GatewayPaymentContext payment, GatewayPaymentState state, string? reference, string? code) =>
        Result.Success(new GatewayCallbackResult(payment.RequestId, payment.Token, reference, state, code));

    private static Result<GatewayReversalResult> NonSuccess(short status, string? message = null) =>
        ParsianStatus.IsKnownRefusal(status)
            ? Result.Failure<GatewayReversalResult>(ParsianStatus.Refusal(status, message))
            : Result.Success(new GatewayReversalResult(GatewayReversalState.Unknown, ProviderCode: ParsianStatus.Code(status), ProviderMessage: message));

    private bool IsOwnContext(GatewayPaymentContext? payment) =>
        payment is not null && string.Equals(payment.ProviderName, Name, StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ToTehranOffset(DateTime? value)
    {
        if (value is not { } date)
            return null;
        var local = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
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
