using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace EwaysVNext.Jobs.Implementation;

/// <summary>
/// The job store as raw SQL. Each operation is one statement, so concurrent workers need no
/// coordination beyond what Postgres already gives: <c>FOR UPDATE SKIP LOCKED</c> hands each row
/// to exactly one claimer, and every ownership-sensitive write carries <c>locked_by</c> in its
/// WHERE clause so a worker that lost its lease silently does nothing.
/// <para>
/// Every statement runs inside <c>CreateExecutionStrategy()</c>: retries are enabled, and a bare
/// <c>ExecuteSqlRawAsync</c> would not be replayed after a transient fault (AGENTS.md rule 8).
/// </para>
/// </summary>
public sealed class PostgresJobStore(DbContext db, IOptions<JobsOptions> options) : IJobStore
{
    public async Task<IReadOnlyList<ClaimedJob>> ClaimAsync(
        string workerId, int batchSize, TimeSpan lease, DateTimeOffset now, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE jobs.job
               SET status = 'Running',
                   locked_by = @worker,
                   locked_until = @now + @lease,
                   attempts = attempts + 1
             WHERE id IN (SELECT id FROM jobs.job
                           WHERE (status = 'Queued' AND run_at <= @now)
                              OR (status = 'Running' AND locked_until < @now)
                           ORDER BY run_at
                             FOR UPDATE SKIP LOCKED
                           LIMIT @batch)
            RETURNING id, type, payload::text, attempts, max_attempts;
            """;

        return await ExecuteAsync(async token =>
        {
            var claimed = new List<ClaimedJob>();

            await using var command = Command(sql,
                Param("worker", workerId),
                Param("now", now),
                Param("lease", lease),
                Param("batch", batchSize));

            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                claimed.Add(new ClaimedJob(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt32(3), reader.GetInt32(4)));
            }

            return (IReadOnlyList<ClaimedJob>)claimed;
        }, ct);
    }

    public async Task<bool> HeartbeatAsync(
        long jobId, string workerId, TimeSpan lease, DateTimeOffset now, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE jobs.job SET locked_until = @now + @lease
             WHERE id = @id AND locked_by = @worker AND status = 'Running';
            """;

        var affected = await ExecuteNonQueryAsync(sql, ct,
            Param("id", jobId), Param("worker", workerId), Param("now", now), Param("lease", lease));

        return affected == 1;
    }

    public Task CompleteAsync(long jobId, string workerId, DateTimeOffset now, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE jobs.job
               SET status = 'Completed', completed_at = @now, locked_by = NULL, locked_until = NULL
             WHERE id = @id AND locked_by = @worker;
            """;

        return ExecuteNonQueryAsync(sql, ct, Param("id", jobId), Param("worker", workerId), Param("now", now));
    }

    public async Task RetryAsync(
        long jobId, string workerId, DateTimeOffset runAt, string reason, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE jobs.job
               SET status = 'Queued', run_at = @runAt, last_error = @reason,
                   locked_by = NULL, locked_until = NULL
             WHERE id = @id AND locked_by = @worker;
            """;

        try
        {
            await ExecuteNonQueryAsync(sql, ct,
                Param("id", jobId), Param("worker", workerId), Param("runAt", runAt), Param("reason", reason));
        }
        catch (PostgresException error) when (error.SqlState == "23505")
        {
            // Requeuing collided with a newer job holding the same dedupe key. That job will do
            // this work, so this one is finished rather than retried — otherwise it could never be
            // requeued and would be claimed forever.
            const string coalesce = """
                UPDATE jobs.job
                   SET status = 'Completed', completed_at = now(),
                       last_error = 'coalesced into queued duplicate',
                       locked_by = NULL, locked_until = NULL
                 WHERE id = @id AND locked_by = @worker;
                """;

            await ExecuteNonQueryAsync(coalesce, ct, Param("id", jobId), Param("worker", workerId));
        }
    }

    public Task FailAsync(long jobId, string workerId, string reason, DateTimeOffset now, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE jobs.job
               SET status = 'Failed', last_error = @reason, completed_at = @now,
                   locked_by = NULL, locked_until = NULL
             WHERE id = @id AND locked_by = @worker;
            """;

        return ExecuteNonQueryAsync(sql, ct,
            Param("id", jobId), Param("worker", workerId), Param("reason", reason), Param("now", now));
    }

    public Task RegisterRecurringAsync(
        string type, TimeSpan interval, DateTimeOffset now, CancellationToken ct = default)
    {
        // The interval is updated on restart so a config change takes effect, but next_run_at is
        // left alone: resetting it on every deploy would make a frequently-restarted service run
        // its recurring jobs far more often than configured.
        const string sql = """
            INSERT INTO jobs.recurring (type, interval, next_run_at)
            VALUES (@type, @interval, @next)
            ON CONFLICT (type) DO UPDATE SET interval = EXCLUDED.interval;
            """;

        return ExecuteNonQueryAsync(sql, ct,
            Param("type", type), Param("interval", interval), Param("next", now + interval));
    }

    public async Task<IReadOnlyList<string>> ClaimDueRecurringAsync(
        DateTimeOffset now, CancellationToken ct = default)
    {
        // Read and advance in one statement: two workers ticking together cannot both see the same
        // job as due.
        const string sql = """
            UPDATE jobs.recurring SET next_run_at = @now + interval
             WHERE next_run_at <= @now
            RETURNING type;
            """;

        return await ExecuteAsync(async token =>
        {
            var due = new List<string>();

            await using var command = Command(sql, Param("now", now));
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) due.Add(reader.GetString(0));

            return (IReadOnlyList<string>)due;
        }, ct);
    }

    private NpgsqlCommand Command(string sql, params NpgsqlParameter[] parameters)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return command;
    }

    private async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct, params NpgsqlParameter[] parameters) =>
        await ExecuteAsync(async token =>
        {
            await using var command = Command(sql, parameters);
            return await command.ExecuteNonQueryAsync(token);
        }, ct);

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async token =>
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await db.Database.OpenConnectionAsync(token);

            return await operation(token);
        }, ct);
    }

    private static NpgsqlParameter Param(string name, object value) => value switch
    {
        TimeSpan interval => new NpgsqlParameter(name, NpgsqlDbType.Interval) { Value = interval },
        DateTimeOffset instant => new NpgsqlParameter(name, NpgsqlDbType.TimestampTz) { Value = instant },
        _ => new NpgsqlParameter(name, value),
    };

    private JobsOptions Options => options.Value;
}
