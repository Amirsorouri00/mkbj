using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Storage.Implementation;

public static class DependencyInjection
{
    public static IServiceCollection AddObjectStorage(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MinioOptions>()
            .Bind(configuration.GetSection(MinioOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.PublicEndpoint))
                    options.PublicEndpoint = options.Endpoint;
            });

        // Singleton: the S3 client is thread-safe and holds a connection pool worth reusing.
        services.TryAddSingleton<IObjectStorage, S3ObjectStorage>();

        services.AddHealthChecks()
            .AddCheck<ObjectStorageHealthCheck>("Storage", tags: ["ready"]);

        return services;
    }
}
