using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Partners.Application.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Partners.Adapter.Outbound.Usage;

/// <summary>
/// Drains the recorder and writes entries in batches.
/// <para>
/// One insert per batch rather than per request: usage rows arrive at the rate of the whole
/// partner API, and a round trip per call would make logging the most expensive thing a request
/// does.
/// </para>
/// <para>
/// The loop never throws out. A failed batch is logged by count and dropped — retrying it would
/// back up the channel behind whatever broke, and usage rows are not worth stalling the queue or
/// crashing the host for. What they are worth is saying out loud that they were lost.
/// </para>
/// </summary>
public sealed class ApiKeyUsageFlushService(
    ChannelApiKeyUsageRecorder recorder,
    IServiceScopeFactory scopeFactory,
    IOptions<PartnerUsageOptions> options,
    ILogger<ApiKeyUsageFlushService> logger) : BackgroundService
{
    private readonly PartnerUsageOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ApiKeyUsageEntry>(_options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FillAsync(batch, stoppingToken);

                if (batch.Count > 0)
                {
                    await WriteAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The whole point: a logging fault must not take the host down.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Dropped {Count} partner usage entries.", batch.Count);
                batch.Clear();
            }
        }

        // A final pass on shutdown recovers whatever is already buffered. Anything that arrives
        // after this is lost; see ADR-0010.
        if (recorder.Reader.TryRead(out var trailing))
        {
            batch.Add(trailing);
            while (batch.Count < _options.BatchSize && recorder.Reader.TryRead(out var more))
                batch.Add(more);

            try
            {
                await WriteAsync(batch, CancellationToken.None);
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Dropped {Count} partner usage entries during shutdown.", batch.Count);
            }
        }
    }

    /// <summary>
    /// Waits for the first entry, then takes whatever else is already queued up to the batch
    /// size. A partial batch is written after <see cref="PartnerUsageOptions.FlushInterval"/> so
    /// a quiet period does not leave one entry sitting unwritten indefinitely.
    /// </summary>
    private async Task FillAsync(List<ApiKeyUsageEntry> batch, CancellationToken ct)
    {
        if (!await recorder.Reader.WaitToReadAsync(ct))
            return;

        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(_options.FlushInterval);

        while (batch.Count < _options.BatchSize)
        {
            if (recorder.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
                continue;
            }

            if (batch.Count > 0)
                break;

            try
            {
                if (!await recorder.Reader.WaitToReadAsync(window.Token))
                    break;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task WriteAsync(List<ApiKeyUsageEntry> batch, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

        db.ApiKeyUsages.AddRange(batch.Select(entry => new ApiKeyUsage
        {
            Id = ids.Generate(),
            PartnerId = entry.PartnerId,
            KeyId = entry.KeyId,
            At = entry.At,
            Method = entry.Method,
            Path = entry.Path,
            Status = entry.Status,
            CorrelationId = entry.CorrelationId,
            DurationMs = entry.DurationMs,
        }));

        await db.SaveChangesAsync(ct);
    }
}
