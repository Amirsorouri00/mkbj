using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Presentation.Endpoint;

public static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        var endpointTypes = assembly.DefinedTypes
            .Where(t => !t.IsAbstract && !t.IsInterface && t.Namespace is not null && typeof(IEndpoint).IsAssignableFrom(t));

        foreach (var type in endpointTypes)
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IEndpoint), type));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, SnowflakeIdJsonOptionsSetup>());

        return services;
    }

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var endpoints = scope.ServiceProvider.GetServices<IEndpoint>();
        foreach (var endpoint in endpoints)
            endpoint.MapEndpoint(app);

        return app;
    }
}
