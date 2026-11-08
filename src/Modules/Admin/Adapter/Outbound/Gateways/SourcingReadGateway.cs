using EwaysVNext.Modules.Admin.Application.Ports.Outbound;
using EwaysVNext.Modules.Admin.Application.Views;
using EwaysVNext.Modules.Sourcing.QueryApplication;

namespace EwaysVNext.Modules.Admin.Adapter.Outbound.Gateways;

/// <summary>
/// Sourcing's counts, in Admin's own words.
/// </summary>
/// <remarks>
/// The rename from "since" to "7d" happens here and nowhere else. Sourcing answers about a
/// window it was given; the dashboard labels it for the person reading it, and the two should
/// not have to agree on a vocabulary.
/// </remarks>
internal sealed class SourcingReadGateway(ISourcingAdminQueries sourcing) : ISourcingReadPort
{
    public async Task<ImportSnapshot> GetDashboardCountsAsync(
        DateTimeOffset since, CancellationToken ct = default)
    {
        var counts = await sourcing.GetDashboardCountsAsync(since, ct);

        return new ImportSnapshot(
            counts.JobsQueued,
            counts.JobsRunning,
            counts.JobsPaused,
            counts.JobsFailedSince,
            counts.JobsCompletedSince,
            counts.RecordsNeedsReview,
            counts.RecordsValidUnpublished,
            counts.RecordsInvalid,
            counts.RecordsPublishedSince);
    }
}
