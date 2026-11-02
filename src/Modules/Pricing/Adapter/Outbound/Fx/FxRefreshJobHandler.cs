using EwaysVNext.Jobs;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound.Fx;

/// <summary>
/// Runs the FX refresh on a schedule, and again whenever an admin asks.
/// <para>
/// Idempotent, as every handler must be: a lease can expire mid-run, and ingesting the same
/// published day twice inserts nothing the second time.
/// </para>
/// </summary>
public sealed class FxRefreshJobHandler(IFxRefreshService refresh) : IJobHandler<FxRefreshPayload>
{
    /// <summary>
    /// Five minutes. The ECB publishes once a day around 16:00 CET, so a failure is almost always
    /// a transient network problem rather than an absent file — long enough not to hammer them,
    /// short enough that a six-hourly schedule is not missed over a blip.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    public string Type => JobTypes.FxRefresh;

    public async Task<JobOutcome> HandleAsync(
        FxRefreshPayload payload, JobContext context, CancellationToken ct = default)
    {
        var result = await refresh.RefreshAsync(ct);

        // RetryLater, never Failed: a provider that is down is not a job that is wrong, and
        // giving up would leave the catalogue quoting from rates that keep ageing.
        return result.IsSuccess
            ? JobOutcome.Completed
            : JobOutcome.RetryLater(RetryDelay, result.Error!.Code);
    }
}
