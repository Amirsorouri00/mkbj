using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EwaysVNext.Storage.Implementation;

/// <summary>
/// Readiness probe for the object store. It checks that a known private bucket answers, so a
/// misconfigured endpoint or a store that has lost its buckets fails the probe instead of
/// surfacing later as a failed invoice download.
/// </summary>
public sealed class ObjectStorageHealthCheck(IObjectStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // A missing key is a healthy answer: it proves the bucket exists and is reachable.
            await storage.ExistsAsync(StorageBuckets.Documents, "__healthcheck__", cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception error)
        {
            return HealthCheckResult.Unhealthy("Object storage is unreachable.", error);
        }
    }
}
