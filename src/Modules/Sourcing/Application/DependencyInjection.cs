using EwaysVNext.Modules.Sourcing.Application.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sourcing.Application;

/// <summary>Marks this assembly, so the installer can hand it to the event registrar.</summary>
public static class SourcingApplicationMarker;

public static class DependencyInjection
{
    public static IServiceCollection AddSourcingApplication(this IServiceCollection services)
    {
        services.AddScoped<RecordProcessor>();
        services.AddScoped<RecordPublisher>();

        return services;
    }
}
