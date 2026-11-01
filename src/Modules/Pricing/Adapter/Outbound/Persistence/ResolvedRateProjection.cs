using System.Globalization;
using System.Text;
using EwaysVNext.Database;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound.Persistence;

internal sealed class ResolvedRateProjection(EwaysVNextDbContext db) : IResolvedRateProjection
{
    private const int ColumnsPerRow = 8;

    private const string Columns =
        "base, quote, rate, provenance, fx_rate_id, fx_override_id, rate_at, valid_until";

    /// <summary>
    /// The types the first VALUES row must be cast to, since Postgres infers a CTE's column
    /// types from it and an untyped literal would come out as <c>text</c>.
    /// </summary>
    private static readonly string[] ColumnTypes =
        ["char(3)", "char(3)", "numeric(18,10)", "text", "bigint", "bigint", "timestamptz", "timestamptz"];

    public async Task ReplaceAllAsync(
        IReadOnlyList<ResolvedRateRow> rows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM pricing.resolved_fx_rates", ct);
            return;
        }

        // One statement, not a delete followed by an insert.
        //
        // Two reasons. A multi-statement command cannot carry parameters through Npgsql's
        // extended protocol; and more importantly, every sub-statement of a single statement
        // sees the same snapshot, so a DELETE-then-INSERT of the same pair would have the insert
        // collide with a row the delete had not yet made invisible. Upserting and then removing
        // what is no longer incoming avoids both.
        var parameters = new List<NpgsqlParameter>(rows.Count * ColumnsPerRow);
        var sql = new StringBuilder("WITH incoming (").Append(Columns).Append(") AS (VALUES ");

        for (var index = 0; index < rows.Count; index++)
        {
            if (index > 0) sql.Append(',');
            AppendRow(sql, index, castTypes: index == 0);
            AddParameters(parameters, rows[index]);
        }

        sql.Append("""
            ), upserted AS (
                INSERT INTO pricing.resolved_fx_rates (base, quote, rate, provenance, fx_rate_id, fx_override_id, rate_at, valid_until)
                SELECT base, quote, rate, provenance, fx_rate_id, fx_override_id, rate_at, valid_until FROM incoming
                ON CONFLICT (base, quote) DO UPDATE SET
                    rate = EXCLUDED.rate,
                    provenance = EXCLUDED.provenance,
                    fx_rate_id = EXCLUDED.fx_rate_id,
                    fx_override_id = EXCLUDED.fx_override_id,
                    rate_at = EXCLUDED.rate_at,
                    valid_until = EXCLUDED.valid_until
                RETURNING base, quote
            )
            DELETE FROM pricing.resolved_fx_rates existing
            WHERE NOT EXISTS (
                SELECT 1 FROM incoming
                WHERE incoming.base = existing.base AND incoming.quote = existing.quote
            )
            """);

        await db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
    }

    private static void AppendRow(StringBuilder sql, int rowIndex, bool castTypes)
    {
        var offset = rowIndex * ColumnsPerRow;
        sql.Append('(');

        for (var column = 0; column < ColumnsPerRow; column++)
        {
            if (column > 0) sql.Append(',');
            sql.Append(CultureInfo.InvariantCulture, $"@p{offset + column}");
            if (castTypes) sql.Append("::").Append(ColumnTypes[column]);
        }

        sql.Append(')');
    }

    /// <summary>
    /// Parameterised, never interpolated. Provenance is a string this process composed today,
    /// but a projection that concatenates its values is one supplier field away from executing
    /// them.
    /// </summary>
    private static void AddParameters(List<NpgsqlParameter> parameters, ResolvedRateRow row)
    {
        // Named, not positional: EF attaches DbParameter instances by name, and $1-style
        // placeholders reach Postgres with nothing bound to them.
        Add(parameters, row.Base, NpgsqlDbType.Char);
        Add(parameters, row.Quote, NpgsqlDbType.Char);
        Add(parameters, row.Rate, NpgsqlDbType.Numeric);
        Add(parameters, row.Provenance, NpgsqlDbType.Text);
        Add(parameters, row.FxRateId, NpgsqlDbType.Bigint);
        Add(parameters, row.FxOverrideId, NpgsqlDbType.Bigint);
        Add(parameters, row.RateAt, NpgsqlDbType.TimestampTz);
        Add(parameters, row.ValidUntil, NpgsqlDbType.TimestampTz);
    }

    private static void Add(List<NpgsqlParameter> parameters, object? value, NpgsqlDbType type) =>
        parameters.Add(new NpgsqlParameter
        {
            ParameterName = $"p{parameters.Count.ToString(CultureInfo.InvariantCulture)}",
            Value = value ?? DBNull.Value,
            NpgsqlDbType = type,
        });
}
