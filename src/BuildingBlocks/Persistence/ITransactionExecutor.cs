using EwaysVNext.Outcomes;
using System;
using System.Collections.Generic;
using System.Text;

namespace EwaysVNext.Persistence;
public interface ITransactionExecutor
{
    Task<Result> ExecuteSerializableAsync(
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken ct = default);

    Task<Result<T>> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        CancellationToken ct = default);
}
