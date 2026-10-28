using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Jobs.Implementation;

/// <summary>Declares that <see cref="HandlerType"/> handles jobs carrying <see cref="PayloadType"/>.</summary>
public sealed record JobHandlerRegistration(Type HandlerType, Type PayloadType);

/// <summary>A recurring job and how often it should run.</summary>
public sealed record RecurringJobRegistration(string Type, TimeSpan Interval);

/// <summary>
/// Maps a job type string to the handler that runs it and the payload type to deserialize into.
/// Built once from the registrations, so the worker neither scans assemblies nor reflects per job.
/// </summary>
public sealed class JobHandlerRegistry
{
    private readonly Dictionary<string, Registration> _byType = new(StringComparer.Ordinal);

    public JobHandlerRegistry(
        IEnumerable<JobHandlerRegistration> registrations, IServiceScopeFactory scopeFactory)
    {
        // Handlers are scoped, so the job-type string is read once from a real instance in a
        // throwaway scope rather than from an uninitialized object.
        using var scope = scopeFactory.CreateScope();

        foreach (var registration in registrations)
        {
            var handlerInterface = typeof(IJobHandler<>).MakeGenericType(registration.PayloadType);
            var typeProperty = handlerInterface.GetProperty(nameof(IJobHandler<object>.Type))!;
            var handleMethod = handlerInterface.GetMethod(nameof(IJobHandler<object>.HandleAsync))!;

            var probe = scope.ServiceProvider.GetRequiredService(registration.HandlerType);
            var jobType = (string)typeProperty.GetValue(probe)!;

            _byType[jobType] = new Registration(
                registration.HandlerType,
                registration.PayloadType,
                async (handler, payload, context, ct) =>
                {
                    try
                    {
                        return await (Task<JobOutcome>)handleMethod.Invoke(handler, [payload, context, ct])!;
                    }
                    catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
                    {
                        // Reflection wraps anything the handler throws before the awaitable is
                        // returned. Left wrapped, every failure would be recorded as "Exception has
                        // been thrown by the target of an invocation" and last_error would be
                        // useless for diagnosing the job.
                        ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
                        throw;
                    }
                });
        }
    }

    public bool TryGet(string type, out Registration registration) => _byType.TryGetValue(type, out registration!);

    public IReadOnlyCollection<string> KnownTypes => _byType.Keys;

    public sealed record Registration(
        Type HandlerServiceType,
        Type PayloadType,
        Func<object, object, JobContext, CancellationToken, Task<JobOutcome>> Invoke);
}
