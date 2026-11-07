using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Domain.Telemetry;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Sales.Adapter.Outbound.Persistence;

/// <summary>
/// Writes one composition-attempt row, in its own scope, on a best-effort basis.
/// </summary>
/// <remarks>
/// <para>
/// Its own scope and its own save, for two reasons. The caller's save is the order, and an
/// attempt row must not join that transaction — a telemetry write failing would roll back a sale.
/// And a <b>failed</b> composition has no caller save at all, so there is nothing to join.
/// </para>
/// <para>
/// Because this table is append-only, independent and idempotent-by-id, the separate save does
/// not breach the one-save-per-operation rule: there is no invariant spanning it and the order.
/// </para>
/// <para>
/// Every exception but cancellation is swallowed and logged. An order that succeeded must not be
/// reported as failed because the dashboard's row could not be written.
/// </para>
/// </remarks>
internal sealed class CompositionAttemptRecorder(
    IServiceScopeFactory scopes,
    IIdGenerator ids,
    ILogger<CompositionAttemptRecorder> logger) : ICompositionAttemptRecorder
{
    public async Task RecordAsync(CompositionAttemptRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

            db.CompositionAttempts.Add(CompositionAttempt.Record(ids.Generate(), record));

            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // The request is going away; there is nothing to report and nobody to report it to.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not record a composition attempt for {Currency} {TargetMinor}.",
                record.Currency,
                record.TargetMinor);
        }
    }
}
