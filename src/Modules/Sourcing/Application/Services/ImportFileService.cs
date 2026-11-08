using System.Globalization;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Ports.Inbound;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using EwaysVNext.Storage;

namespace EwaysVNext.Modules.Sourcing.Application.Services;

/// <summary>
/// Uploading a file against a draft job, and saying what is in it.
/// </summary>
/// <remarks>
/// <para>
/// The file is stored before it is understood. An upload that failed halfway through parsing
/// would otherwise have to be done again, and a supplier's catalogue is not a small thing to
/// send twice.
/// </para>
/// <para>
/// The preview exists so that mapping is a choice rather than a guess. Showing column names
/// alone makes somebody decide what <c>col_3</c> means from its heading, and a column mapped to
/// the wrong field produces a catalogue that looks right.
/// </para>
/// </remarks>
internal sealed class ImportFileService(
    IImportJobRepository jobs,
    IEnumerable<IImportFileReader> readers,
    IObjectStorage storage,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    IClock clock) : IImportFileService
{
    /// <summary>How many rows a mapping screen shows.</summary>
    public const int SampleSize = 5;

    private const string TargetType = "ImportJob";

    public async Task<Result<ImportFilePreview>> UploadAsync(
        long jobId,
        Stream content,
        ImportFileFormat format,
        AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(actor);

        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) return Result.Failure<ImportFilePreview>(SourcingErrors.JobNotFound);

        if (job.Status != ImportJobStatus.Draft)
            return Result.Failure<ImportFilePreview>(SourcingErrors.JobStateConflict);

        var reader = readers.FirstOrDefault(candidate => candidate.Handles(format));
        if (reader is null)
            return Result.Failure<ImportFilePreview>(SourcingErrors.FileFormatUnsupported);

        // Buffered because the file is read twice: once to store it, once to describe it. The
        // uploads are measured in megabytes, not gigabytes.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        var key = ObjectKey(jobId, format);

        buffer.Position = 0;
        await storage.PutAsync(
            StorageBuckets.ImportStaging, key, buffer, ContentType(format), ct);

        buffer.Position = 0;
        var contents = await reader.ReadAsync(buffer, SampleSize, ct);

        if (contents.IsFailure) return Result.Failure<ImportFilePreview>(contents.Error!);

        job.AttachFile(key, format, clock.UtcNow);

        audit.Record(
            actor.ToAudit(), "sourcing.import_job.file_attached", TargetType,
            jobId.ToString(CultureInfo.InvariantCulture), null, new { Key = key, Format = format });

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ImportFilePreview(
            contents.Value!.Columns,
            [.. contents.Value.Rows.Select(row => row.Cells)],
            SampleSize));
    }

    public async Task<Result> SetMappingAsync(
        long jobId,
        IReadOnlyDictionary<string, string> mapping,
        AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(actor);

        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) return Result.Failure(SourcingErrors.JobNotFound);

        if (job.Status != ImportJobStatus.Draft)
            return Result.Failure(SourcingErrors.JobStateConflict);

        var unknown = mapping.Values
            .Where(field => !ImportColumns.IsKnown(field))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unknown.Count > 0)
        {
            // Named rather than ignored: a mapping with a typo'd field silently drops that
            // column, and the import looks successful.
            return Result.Failure(SourcingErrors.ValidationFailed(
                $"These are not import fields: {string.Join(", ", unknown)}."));
        }

        try
        {
            job.SetMapping(new ImportColumnMapping(mapping), clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(ImportJobService.FromDomain(exception));
        }

        audit.Record(
            actor.ToAudit(), "sourcing.import_job.mapping_set", TargetType,
            jobId.ToString(CultureInfo.InvariantCulture), null, new { Fields = mapping.Count });

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <remarks>
    /// Keyed by job, so one job's uploads sit together and a replaced file overwrites rather
    /// than accumulating.
    /// </remarks>
    private static string ObjectKey(long jobId, ImportFileFormat format) =>
        $"jobs/{jobId.ToString(CultureInfo.InvariantCulture)}/source.{Extension(format)}";

    private static string Extension(ImportFileFormat format) => format switch
    {
        ImportFileFormat.Csv => "csv",
        ImportFileFormat.Xlsx => "xlsx",
        _ => "json",
    };

    private static string ContentType(ImportFileFormat format) => format switch
    {
        ImportFileFormat.Csv => "text/csv",
        ImportFileFormat.Xlsx =>
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/json",
    };
}
