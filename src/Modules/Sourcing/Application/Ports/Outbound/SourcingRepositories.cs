using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourceListings;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;

namespace EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;

public interface IImportJobRepository
{
    Task<ImportJob?> GetAsync(long jobId, CancellationToken ct = default);

    Task AddAsync(ImportJob job, CancellationToken ct = default);
}

public interface IStagedRecordRepository
{
    Task<StagedRecord?> GetAsync(long recordId, CancellationToken ct = default);

    /// <summary>The record for this fingerprint, if anything has ever staged it.</summary>
    Task<StagedRecord?> FindByFingerprintAsync(string fingerprint, CancellationToken ct = default);

    Task AddAsync(StagedRecord record, CancellationToken ct = default);

    /// <summary>
    /// The next batch of this job's records past a high-water mark.
    /// </summary>
    /// <remarks>
    /// Keyed on the id rather than a page number, because the pipeline stages while it
    /// processes: an offset page would skip records that arrived in between.
    /// </remarks>
    Task<IReadOnlyList<StagedRecord>> NextForProcessingAsync(
        long jobId, long afterRecordId, int batchSize, CancellationToken ct = default);
}

public interface ISourceListingRepository
{
    Task<SourceListing?> FindByUrlAsync(string url, CancellationToken ct = default);

    Task AddAsync(SourceListing listing, CancellationToken ct = default);
}

public interface ISourcePolicyRepository
{
    Task<SourcePolicy?> FindByDomainAsync(string domain, CancellationToken ct = default);

    Task<IReadOnlyList<SourcePolicy>> ListAsync(CancellationToken ct = default);

    Task AddAsync(SourcePolicy policy, CancellationToken ct = default);
}
