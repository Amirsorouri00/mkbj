using EwaysVNext.Modules.Sourcing.Application.Pipeline;
using EwaysVNext.Modules.Sourcing.Application.Ports.Inbound;
using EwaysVNext.Modules.Sourcing.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sourcing.Application;

/// <summary>Marks this assembly, so the installer can hand it to the event registrar.</summary>
public static class SourcingApplicationMarker;

public static class DependencyInjection
{
    public static IServiceCollection AddSourcingApplication(this IServiceCollection services)
    {
        services.AddScoped<StagingService>();
        services.AddScoped<RecordProcessor>();
        services.AddScoped<RecordPublisher>();

        services.AddScoped<IImportPipeline, ImportPipeline>();
        services.AddScoped<IImportJobService, ImportJobService>();
        services.AddScoped<IImportFileService, ImportFileService>();
        services.AddScoped<IStagedRecordReviewService, StagedRecordReviewService>();
        services.AddScoped<ISourcePolicyService, SourcePolicyService>();

        return services;
    }
}
