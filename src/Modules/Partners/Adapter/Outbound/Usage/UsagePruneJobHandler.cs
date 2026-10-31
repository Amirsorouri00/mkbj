using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using EwaysVNext.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Partners.Adapter.Outbound.Usage;

public sealed record UsagePrunePayload;

/// <summary>
/// Deletes usage rows past the retention window.
/// <para>
/// Idempotent by construction: it deletes by age rather than by a cursor, so running it twice,
/// or re-running it after a lease expired mid-delete, removes the same set and nothing more.
/// </para>
/// <para>
/// <c>ExecuteDeleteAsync</c> rather than loading and removing: the point is to delete a large
/// number of rows, and materialising them first would pull ninety days of request history into
/// memory to throw it away.
/// </para>
/// </summary>
public sealed class UsagePruneJobHandler(
    EwaysVNextDbContext db,
    IClock clock,
    IOptions<PartnerUsageOptions> options,
    ILogger<UsagePruneJobHandler> logger) : IJobHandler<UsagePrunePayload>
{
    public string Type => JobTypes.UsagePrune;

    public async Task<JobOutcome> HandleAsync(
        UsagePrunePayload payload, JobContext context, CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow - options.Value.Retention;

        var deleted = await db.ApiKeyUsages
            .Where(usage => usage.At < cutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "Pruned {Deleted} partner usage rows older than {Cutoff:o}.", deleted, cutoff);

        return JobOutcome.Completed;
    }
}
