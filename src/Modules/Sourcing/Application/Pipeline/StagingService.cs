using System.Text.Json;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Sourcing.Application.Pipeline;

/// <summary>What one discovery batch did.</summary>
public sealed record StagingResult(
    int Discovered, int Staged, int Duplicate, string? Cursor, bool More);

/// <summary>
/// Turns discovered listings into staged records, once each.
/// </summary>
/// <remarks>
/// <para>
/// The whole of "import twice, stage once" runs through here: each listing is fingerprinted,
/// looked up, and either staged fresh or handed to the existing record to refresh. A listing
/// seen before does not become a second record whatever found it.
/// </para>
/// <para>
/// The lookup-then-insert race is real — two workers on one listing both find nothing — and the
/// unique index on the fingerprint is what actually decides it. This code takes the optimistic
/// path and lets the database be right.
/// </para>
/// </remarks>
public sealed class StagingService(
    IStagedRecordRepository records, IIdGenerator ids, IClock clock)
{
    public async Task<StagingResult> StageAsync(
        ImportJob job, DiscoveryBatch batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(batch);

        var now = clock.UtcNow;
        var staged = 0;
        var duplicate = 0;

        foreach (var listing in batch.Listings)
        {
            ct.ThrowIfCancellationRequested();

            var fingerprint = Fingerprint.For(listing);
            var raw = JsonSerializer.Serialize(listing);

            var existing = await records.FindByFingerprintAsync(fingerprint.Value, ct);

            if (existing is null)
            {
                await records.AddAsync(
                    StagedRecord.Stage(ids.Generate(), job.Id, fingerprint, listing, raw, now), ct);

                staged++;
                continue;
            }

            var outcome = existing.Restage(job.Id, listing, raw, now);

            // Only a listing a previous job staged counts as a duplicate. The same page reached
            // twice within one crawl has discovered nothing, and counting it would make the
            // duplicate figure a measure of the crawl's link structure.
            if (outcome == RestageOutcome.Duplicate) duplicate++;
        }

        return new StagingResult(
            batch.Listings.Count, staged, duplicate, batch.Cursor, batch.More);
    }
}
