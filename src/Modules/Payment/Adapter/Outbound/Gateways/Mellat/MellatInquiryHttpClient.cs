using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat.Contracts;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;

internal sealed class MellatInquiryHttpClient(HttpClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<Result<MellatInquiryResponse>> GetByReferenceAsync(
        MellatInquiryRequest payload, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    "bhrws/transactionInfo/getTransactionById")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                using var response = await client.SendAsync(request, ct);
                ct.ThrowIfCancellationRequested();
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<MellatInquiryResponse>(new Error(
                        "MELLAT_INQUIRY_HTTP_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                        "Mellat returned an HTTP error for transaction inquiry.", 502));
                }

                var body = await response.Content.ReadFromJsonAsync<MellatInquiryResponse>(JsonOptions, ct);
                return body is null
                    ? Result.Failure<MellatInquiryResponse>(PaymentGatewayErrors.InvalidResponse)
                    : Result.Success(body);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                if (attempt == 1)
                    return Result.Failure<MellatInquiryResponse>(PaymentGatewayErrors.Timeout);
            }
            catch (HttpRequestException)
            {
                ct.ThrowIfCancellationRequested();
                if (attempt == 1)
                    return Result.Failure<MellatInquiryResponse>(PaymentGatewayErrors.Unavailable);
            }
            catch (IOException)
            {
                ct.ThrowIfCancellationRequested();
                if (attempt == 1)
                    return Result.Failure<MellatInquiryResponse>(PaymentGatewayErrors.Unavailable);
            }
            catch (JsonException)
            {
                return Result.Failure<MellatInquiryResponse>(PaymentGatewayErrors.InvalidResponse);
            }
        }

        return Result.Failure<MellatInquiryResponse>(PaymentGatewayErrors.Unavailable);
    }
}
