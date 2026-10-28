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

        return services;
    }
}
