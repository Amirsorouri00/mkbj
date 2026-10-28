using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

internal sealed class ParsianHttpClient(HttpClient client)
{
    // Preserve the documented field casing and numeric types; missing Status is nullable in wire DTOs.
    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<Result<TResponse>> PostAsync<TRequest, TResponse>(string path, TRequest payload, bool retryTransport, CancellationToken ct)
        where TResponse : class
    {
        int attempts = retryTransport ? 2 : 1;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // HttpRequestMessage and content cannot be reused after SendAsync, including retries.
                using var request = new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                using var response = await client.SendAsync(request, ct);
                ct.ThrowIfCancellationRequested();
               
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<TResponse>(new Error(
                        "PARSIAN_HTTP_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                        "The provider returned an HTTP error; the payment outcome is unknown.", 502));
                }
                
                var body = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
                ct.ThrowIfCancellationRequested();
                return body is null ? Result.Failure<TResponse>(PaymentGatewayErrors.InvalidResponse) : Result.Success(body);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                if (attempt == attempts - 1) return Result.Failure<TResponse>(PaymentGatewayErrors.Timeout);
            }
            catch (HttpRequestException)
            {
                ct.ThrowIfCancellationRequested();
                if (attempt == attempts - 1) return Result.Failure<TResponse>(PaymentGatewayErrors.Unavailable);
            }
            catch (IOException)
            {
                ct.ThrowIfCancellationRequested();
                if (attempt == attempts - 1) return Result.Failure<TResponse>(PaymentGatewayErrors.Unavailable);
            }
            catch (JsonException)
            {
                ct.ThrowIfCancellationRequested();
                return Result.Failure<TResponse>(PaymentGatewayErrors.InvalidResponse);
            }
        }
        return Result.Failure<TResponse>(PaymentGatewayErrors.Unavailable);
    }
}
