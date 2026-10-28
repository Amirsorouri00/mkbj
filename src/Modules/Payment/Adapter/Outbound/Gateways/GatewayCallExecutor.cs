using EwaysVNext.Outcomes;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;

internal sealed class GatewayCallExecutor(TimeProvider timeProvider, ILogger<GatewayCallExecutor> logger) : IGatewayCallExecutor
{
    public async Task<Result<T>> ExecuteAsync<T>(string providerName,
        Func<CancellationToken, Task<Result<T>>> call, TimeSpan? timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var pending = call(attempt.Token);
            var result = timeout.HasValue
                ? await pending.WaitAsync(timeout.Value, timeProvider, ct)
                : await pending.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            attempt.Cancel();
            logger.LogWarning("Payment gateway {Provider} response timed out", providerName);
            return Result.Failure<T>(PaymentGatewayErrors.Timeout);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Payment gateway {Provider} cancelled its operation", providerName);
            return Result.Failure<T>(PaymentGatewayErrors.Timeout);
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Payment gateway {Provider} transport failed", providerName);
            return Result.Failure<T>(PaymentGatewayErrors.Unavailable);
        }
    }
}
