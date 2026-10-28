using System.Text.Json;
using EwaysVNext.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace EwaysVNext.Jobs.Implementation;

public sealed class PostgresJobQueue(
    DbContext db,
    IIdGenerator idGenerator,
    IClock clock,
    IOptions<JobsOptions> options,
    ILogger<PostgresJobQueue> logger) : IJobQueue
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public async Task EnqueueAsync<TPayload>(
        string type,
        TPayload payload,
        DateTimeOffset? runAt = null,
        string? dedupeKey = null,
        CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql())
        {
            // The dev InMemory fallback has no jobs table. Warn rather than throw: a developer
            // running without a database should still be able to exercise the write paths.
            logger.LogWarning(
                "Job {JobType} was not enqueued: the configured provider {Provider} is not PostgreSQL.",
                type, db.Database.ProviderName);
            return;
        }

        const string sql = """
            INSERT INTO jobs.job
                (id, type, payload, status, run_at, attempts, max_attempts, dedupe_key, created_at)
            VALUES
                (@id, @type, @payload::jsonb, 'Queued', @runAt, 0, @maxAttempts, @dedupeKey, @now)
            ON CONFLICT (dedupe_key) WHERE status = 'Queued' DO NOTHING;
            """;

        var now = clock.UtcNow;
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async token =>
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await db.Database.OpenConnectionAsync(token);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", idGenerator.Generate());
            command.Parameters.AddWithValue("type", type);
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(payload, PayloadJson));
            command.Parameters.Add(new NpgsqlParameter("runAt", NpgsqlDbType.TimestampTz) { Value = runAt ?? now });
            command.Parameters.AddWithValue("maxAttempts", options.Value.MaxAttempts);
            command.Parameters.AddWithValue("dedupeKey", (object?)dedupeKey ?? DBNull.Value);
            command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });

            await command.ExecuteNonQueryAsync(token);
        }, ct);
    }
}
