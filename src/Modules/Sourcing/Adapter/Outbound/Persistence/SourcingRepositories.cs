using EwaysVNext.Database;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourceListings;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Persistence;

internal sealed class ImportJobRepository(EwaysVNextDbContext db) : IImportJobRepository
{
    public Task<ImportJob?> GetAsync(long jobId, CancellationToken ct = default) =>
        db.ImportJobs.FirstOrDefaultAsync(job => job.Id == jobId, ct);

    public Task AddAsync(ImportJob job, CancellationToken ct = default)
    {
        db.ImportJobs.Add(job);
        return Task.CompletedTask;
    }
}

internal sealed class StagedRecordRepository(EwaysVNextDbContext db) : IStagedRecordRepository
{
    public Task<StagedRecord?> GetAsync(long recordId, CancellationToken ct = default) =>
        db.StagedRecords.FirstOrDefaultAsync(record => record.Id == recordId, ct);

    public Task<StagedRecord?> FindByFingerprintAsync(
        string fingerprint, CancellationToken ct = default) =>
        db.StagedRecords.FirstOrDefaultAsync(record => record.Fingerprint == fingerprint, ct);

    public Task AddAsync(StagedRecord record, CancellationToken ct = default)
    {
        db.StagedRecords.Add(record);
        return Task.CompletedTask;
    }

    /// <remarks>
    /// Ordered by id and taken past a high-water mark, so a resumed job continues where it
    /// stopped and a record staged while processing was under way is still picked up.
    /// </remarks>
    public async Task<IReadOnlyList<StagedRecord>> NextForProcessingAsync(
        long jobId, long afterRecordId, int batchSize, CancellationToken ct = default) =>
        await db.StagedRecords
            .Where(record => record.LastJobId == jobId
                && record.Id > afterRecordId
                && record.Status == StagedRecordStatus.Staged)
            .OrderBy(record => record.Id)
            .Take(batchSize)
            .ToListAsync(ct);
}

internal sealed class SourceListingRepository(EwaysVNextDbContext db) : ISourceListingRepository
{
    public Task<SourceListing?> FindByUrlAsync(string url, CancellationToken ct = default) =>
        db.SourceListings.FirstOrDefaultAsync(listing => listing.Url == url, ct);

    public Task AddAsync(SourceListing listing, CancellationToken ct = default)
    {
        db.SourceListings.Add(listing);
        return Task.CompletedTask;
    }
}

internal sealed class SourcePolicyRepository(EwaysVNextDbContext db) : ISourcePolicyRepository
{
    public Task<SourcePolicy?> FindByDomainAsync(string domain, CancellationToken ct = default)
    {
        // Normalised on the way in as well as on the way out: a lookup by raw host text would
        // miss the policy that governs it.
        var key = SourceDomain.Normalize(domain);

        return db.SourcePolicies.FirstOrDefaultAsync(policy => policy.Domain == key, ct);
    }

    public async Task<IReadOnlyList<SourcePolicy>> ListAsync(CancellationToken ct = default) =>
        await db.SourcePolicies.AsNoTracking().OrderBy(policy => policy.Domain).ToListAsync(ct);

    public Task AddAsync(SourcePolicy policy, CancellationToken ct = default)
    {
        db.SourcePolicies.Add(policy);
        return Task.CompletedTask;
    }
}
