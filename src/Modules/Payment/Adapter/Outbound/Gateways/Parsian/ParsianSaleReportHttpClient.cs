using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

internal sealed class ParsianSaleReportHttpClient(
    HttpClient client,
    IParsianInquiryAccessTokenProvider? tokenProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<Result<SaleReportResponse>> GetSaleReportAsync(
        SaleReportRequest payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (tokenProvider is null)
            return Result.Failure<SaleReportResponse>(ParsianInquiryErrors.TokenProviderUnavailable);

        var tokenResult = await tokenProvider.GetValidTokenAsync(ct);
        if (tokenResult.IsFailure)
            return Result.Failure<SaleReportResponse>(tokenResult.Error!);
        if (string.IsNullOrWhiteSpace(tokenResult.Value))
            return Result.Failure<SaleReportResponse>(ParsianInquiryErrors.InvalidToken);

        var token = tokenResult.Value;
        var first = await SendAsync(payload, token, ct);
        if (first.IsFailure)
            return Result.Failure<SaleReportResponse>(first.Error!);
        if (!first.Value!.AuthorizationRejected)
            return Result.Success(first.Value.Response!);

        var renewed = await tokenProvider.RenewRejectedTokenAsync(token, ct);
        if (renewed.IsFailure)
            return Result.Failure<SaleReportResponse>(renewed.Error!);
        if (string.IsNullOrWhiteSpace(renewed.Value))
            return Result.Failure<SaleReportResponse>(ParsianInquiryErrors.InvalidToken);

        var second = await SendAsync(payload, renewed.Value, ct);
        if (second.IsFailure)
            return Result.Failure<SaleReportResponse>(second.Error!);
        return second.Value!.AuthorizationRejected
            ? Result.Failure<SaleReportResponse>(ParsianInquiryErrors.AuthorizationRejected)
            : Result.Success(second.Value.Response!);
    }

    private async Task<Result<ReportAttempt>> SendAsync(
        SaleReportRequest payload, string token, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/PGWReport/GetSaleReport")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await client.SendAsync(request, ct);
                ct.ThrowIfCancellationRequested();
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return Result.Success(new ReportAttempt(AuthorizationRejected: true, Response: null));
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<ReportAttempt>(new Error(
                        "PARSIAN_INQUIRY_HTTP_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                        "Parsian returned an HTTP error for transaction inquiry.", 502));
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                var parsed = SaleReportDataReader.Read(json);
                return parsed.IsFailure
                    ? Result.Failure<ReportAttempt>(parsed.Error!)
                    : Result.Success(new ReportAttempt(AuthorizationRejected: false, parsed.Value));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                if (attempt == 1)
                    return Result.Failure<ReportAttempt>(PaymentGatewayErrors.Timeout);
            }
            catch (HttpRequestException)
            {
                ct.ThrowIfCancellationRequested();
                if (attempt == 1)
                    return Result.Failure<ReportAttempt>(PaymentGatewayErrors.Unavailable);
            }
            catch (IOException)
            {
                ct.ThrowIfCancellationRequested();
                if (attempt == 1)
                    return Result.Failure<ReportAttempt>(PaymentGatewayErrors.Unavailable);
            }
            catch (JsonException)
            {
                return Result.Failure<ReportAttempt>(PaymentGatewayErrors.InvalidResponse);
            }
        }

        return Result.Failure<ReportAttempt>(PaymentGatewayErrors.Unavailable);
    }

    private sealed record ReportAttempt(bool AuthorizationRejected, SaleReportResponse? Response);
}
