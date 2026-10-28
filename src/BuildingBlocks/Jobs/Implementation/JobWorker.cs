using System.Text.Json;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Jobs.Implementation;

/// <summary>
/// Claims jobs and runs them. Each job gets its own DI scope — and so its own DbContext — because
/// one job's failed save must not poison the next job's change tracker.
/// <para>
/// Delivery is <b>at-least-once</b>: a lease can expire while a handler is still working, and the
/// job will then be claimed again. Every handler must be idempotent.
/// </para>
/// </summary>
public sealed class JobWorker(
    IServiceScopeFactory scopeFactory,
    JobHandlerRegistry registry,
    IEnumerable<RecurringJobRegistration> recurring,
    IClock clock,
    IOptions<JobsOptions> options,
    ILogger<JobWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private JobsOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Checked here as well as at registration: AddJobs reads configuration before any source
        // layered on later (a test host, a reloaded file) exists, so registration alone is not a
        // reliable off switch.
        if (Options.Mode == JobsMode.Disabled)
        {
            logger.LogInformation("Jobs:Mode is Disabled; the worker loop will not run.");
            return;
        }

        await RegisterRecurringAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnqueueDueRecurringAsync(stoppingToken);
                var processed = await RunOnceAsync(stoppingToken);

                // Only idle when there was nothing to do: a full batch means more is waiting.
                if (processed == 0)
                    await Task.Delay(Options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                // The loop must outlive any single failure, including the database being down.
                logger.LogError(error, "The job worker loop failed; retrying after the poll interval.");
                await Task.Delay(Options.PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>Claims one batch and runs it. Returns how many jobs were processed.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var claimScope = scopeFactory.CreateAsyncScope();
        var store = claimScope.ServiceProvider.GetRequiredService<IJobStore>();

        var claimed = await store.ClaimAsync(
            Options.WorkerId, Options.BatchSize, Options.LeaseDuration, clock.UtcNow, ct);

        foreach (var job in claimed) await RunAsync(job, ct);

        return claimed.Count;
    }

    private async Task RunAsync(ClaimedJob job, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();

        // Ties every log line and audit row the handler writes back to this job.
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().CorrelationId = $"job-{job.Id}";

        if (job.Attempts > job.MaxAttempts)
        {
            // Re-claimed past its budget after a lease expiry: stop here rather than run it again.
            await store.FailAsync(job.Id, Options.WorkerId, "max attempts exceeded", clock.UtcNow, ct);
            return;
        }

        if (!registry.TryGet(job.Type, out var registration))
        {
            await store.FailAsync(
                job.Id, Options.WorkerId, $"no handler registered for job type '{job.Type}'", clock.UtcNow, ct);
            return;
        }

        try
        {
            var payload = JsonSerializer.Deserialize(job.Payload, registration.PayloadType, PayloadJson)
                ?? Activator.CreateInstance(registration.PayloadType)!;
            var handler = scope.ServiceProvider.GetRequiredService(registration.HandlerServiceType);

            var context = new JobContext(job.Id, job.Attempts, async token =>
            {
                var alive = await store.HeartbeatAsync(
                    job.Id, Options.WorkerId, Options.LeaseDuration, clock.UtcNow, token);
                if (!alive) throw new JobLeaseLostException(job.Id);
            });

            var outcome = await registration.Invoke(handler, payload, context, ct);

            switch (outcome)
            {
                case JobOutcome.CompletedOutcome:
                    await store.CompleteAsync(job.Id, Options.WorkerId, clock.UtcNow, ct);
                    break;

                case JobOutcome.RetryLaterOutcome retry:
                    await store.RetryAsync(
                        job.Id, Options.WorkerId, clock.UtcNow + retry.Delay, retry.Reason, ct);
                    break;

                case JobOutcome.FailedOutcome failed:
                    await store.FailAsync(job.Id, Options.WorkerId, failed.Reason, clock.UtcNow, ct);
                    break;
            }
        }
        catch (JobLeaseLostException lost)
        {
            // Another worker owns this job now. Writing anything would overwrite its progress.
            logger.LogWarning("Abandoning job {JobId}: the lease was lost to another worker.", lost.JobId);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Job {JobId} of type {JobType} threw.", job.Id, job.Type);

            if (job.Attempts >= job.MaxAttempts)
            {
                await store.FailAsync(job.Id, Options.WorkerId, error.Message, clock.UtcNow, ct);
            }
            else
            {
                var delay = JobBackoff.For(job.Attempts, Options.BaseRetryDelay, Options.MaxRetryDelay);
                await store.RetryAsync(job.Id, Options.WorkerId, clock.UtcNow + delay, error.Message, ct);
            }
        }
    }

    private async Task RegisterRecurringAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();

        foreach (var registration in recurring)
            await store.RegisterRecurringAsync(registration.Type, registration.Interval, clock.UtcNow, ct);
    }

    private async Task EnqueueDueRecurringAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        foreach (var type in await store.ClaimDueRecurringAsync(clock.UtcNow, ct))
        {
            // Dedupe key: a tick that arrives while the previous run is still queued is dropped
            // rather than piling up a backlog of identical sweeps.
            await queue.EnqueueAsync(
                type, EmptyPayload.Instance, clock.UtcNow, $"recurring:{type}", ct);
        }
    }
}
