using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;

public interface IGatewayCallExecutor
{
    Task<Result<T>> ExecuteAsync<T>(string providerName,
        Func<CancellationToken, Task<Result<T>>> call, TimeSpan? timeout, CancellationToken ct);
}
