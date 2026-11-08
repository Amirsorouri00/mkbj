using System.Globalization;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Storage;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Files;

/// <summary>
/// Reads an uploaded file as a source of listings.
/// </summary>
/// <remarks>
/// The cursor is the row number, so a file import resumes exactly where it stopped. The file is
/// re-read from object storage each batch rather than held open: a job can be paused for a day
/// and a stream would not survive that, nor should a worker hold one while it does other work.
/// </remarks>
internal sealed class FileImportAdapter(
    IEnumerable<IImportFileReader> readers,
    IObjectStorage storage,
    IClock clock,
    ILogger<FileImportAdapter> logger) : ISourceAdapter
{
    public bool Handles(ImportMode mode) => mode == ImportMode.File;

    public async Task<DiscoveryBatch> DiscoverAsync(
        ImportJobSpec spec, string? cursor, int batchSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.FileObjectKey is null || spec.FileFormat is not { } format || spec.Mapping is null)
        {
            // The aggregate refuses to start such a job, so reaching here means something else
            // went wrong; an empty batch ends the job rather than looping forever.
            logger.LogWarning("Job {JobId} has no readable file.", spec.JobId);
            return new DiscoveryBatch([], null, false);
        }

        var reader = readers.FirstOrDefault(candidate => candidate.Handles(format));
        if (reader is null) return new DiscoveryBatch([], null, false);

        await using var content = await storage.GetAsync(
            StorageBuckets.ImportStaging, spec.FileObjectKey, ct);

        if (content is null)
        {
            logger.LogWarning(
                "Job {JobId} refers to {Key}, which is not in storage.",
                spec.JobId, spec.FileObjectKey);

            return new DiscoveryBatch([], null, false);
        }

        var from = int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        // Read up to the end of this batch, then skip what was already done. Readers are
        // forward-only, and a file small enough to upload is small enough to re-read.
        var contents = await reader.ReadAsync(content, from + batchSize, ct);
        if (contents.IsFailure) return new DiscoveryBatch([], null, false);

        var rows = contents.Value!.Rows.Skip(from).Take(batchSize).ToList();
        var now = clock.UtcNow;

        var listings = rows
            .Select(row => ImportRowMapper.Map(
                spec.JobId, row.Number, row.Cells, spec.Mapping, Method(format), now))
            .ToList();

        var next = from + rows.Count;

        return new DiscoveryBatch(
            listings,
            next.ToString(CultureInfo.InvariantCulture),
            rows.Count == batchSize);
    }

    private static ExtractionMethod Method(ImportFileFormat format) => format switch
    {
        ImportFileFormat.Csv => ExtractionMethod.Csv,
        ImportFileFormat.Xlsx => ExtractionMethod.Xlsx,
        _ => ExtractionMethod.Json,
    };
}
