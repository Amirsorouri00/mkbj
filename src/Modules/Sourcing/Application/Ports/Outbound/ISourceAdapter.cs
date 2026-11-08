using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;

namespace EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;

/// <summary>Where one batch of discovery got to.</summary>
/// <remarks>
/// <c>Cursor</c> is opaque to everything but the adapter that issued it. The pipeline stores it
/// and hands it back, which is what lets a crawl resume mid-site after the process died without
/// the pipeline knowing anything about crawl frontiers.
/// </remarks>
public sealed record DiscoveryBatch(
    IReadOnlyList<RawListing> Listings, string? Cursor, bool More);

/// <summary>
/// Finds listings, a batch at a time.
/// </summary>
/// <remarks>
/// Every source — a crawl, a search, an uploaded file — implements this and nothing else, so the
/// pipeline behind it is one thing to test rather than three. A batch at a time rather than an
/// enumerable to the end: an adapter that streamed would hold the job open across a pause.
/// </remarks>
public interface ISourceAdapter
{
    bool Handles(ImportMode mode);

    Task<DiscoveryBatch> DiscoverAsync(
        ImportJobSpec spec, string? cursor, int batchSize, CancellationToken ct = default);
}
