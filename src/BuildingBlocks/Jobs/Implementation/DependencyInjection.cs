using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Jobs.Implementation;

public static class DependencyInjection
{
    public static IServiceCollection AddJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JobsOptions>().Bind(configuration.GetSection(JobsOptions.SectionName));

        services.TryAddScoped<IJobStore, PostgresJobStore>();
        services.TryAddScoped<IJobQueue, PostgresJobQueue>();
        services.TryAddSingleton<JobHandlerRegistry>();

        var mode = configuration.GetValue($"{JobsOptions.SectionName}:{nameof(JobsOptions.Mode)}", JobsMode.InProcess);
        if (mode is JobsMode.InProcess or JobsMode.WorkerOnly)
        {
            // Also registered as a concrete singleton so tests can drive RunOnceAsync directly
            // instead of waiting on the poll loop.
            services.TryAddSingleton<JobWorker>();
            services.AddHostedService(sp => sp.GetRequiredService<JobWorker>());
        }

        return services;
    }

    public static IServiceCollection AddJobHandler<THandler, TPayload>(this IServiceCollection services)
        where THandler : class, IJobHandler<TPayload>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new JobHandlerRegistration(typeof(THandler), typeof(TPayload)));
        return services;
    }

    public static IServiceCollection AddRecurringJob(this IServiceCollection services, string type, TimeSpan interval)
    {
        services.AddSingleton(new RecurringJobRegistration(type, interval));
        return services;
    }
}
