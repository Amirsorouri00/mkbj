using EwaysVNext.Outcomes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EwaysVNext.Persistence;

public sealed class EfCoreTransactionExecutor<TDbContext>(
    TDbContext db)
    : ITransactionExecutor
    where TDbContext : DbContext
{
    public async Task<Result> ExecuteSerializableAsync(
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken ct = default)
    {
        try
        {
            return await ExecuteInTransactionAsync(operation, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Error.ConcurrencyConflict);
        }
        catch (DbUpdateException error)
        {
            return Result.Failure(ToError(error));
        }
    }

    public async Task<Result<T>> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        CancellationToken ct = default)
    {
        try
        {
            return await ExecuteInTransactionAsync(operation, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<T>(Error.ConcurrencyConflict);
        }
        catch (DbUpdateException error)
        {
            return Result.Failure<T>(ToError(error));
        }
    }

    private async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational() ||
            db.Database.CurrentTransaction is not null)
        {
            return await operation(ct);
        }

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async token =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    token);

            try
            {
                var result = await operation(token);

                if (result is Result { IsFailure: true })
                    await transaction.RollbackAsync(token);
                else
                    await transaction.CommitAsync(token);

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(token);
                throw;
            }
        }, ct);
    }

    private static Error ToError(DbUpdateException error)
    {
        if (error.InnerException is SqlException
            {
                Number: 2601 or 2627
            })
        {
            return Error.Conflict;
        }

        return Error.PersistenceConflict;
    }
}
