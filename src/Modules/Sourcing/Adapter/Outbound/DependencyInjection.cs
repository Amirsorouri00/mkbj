using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Persistence;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddSourcingOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<IImportJobRepository, ImportJobRepository>();
        services.AddScoped<IStagedRecordRepository, StagedRecordRepository>();
        services.AddScoped<ISourceListingRepository, SourceListingRepository>();
        services.AddScoped<ISourcePolicyRepository, SourcePolicyRepository>();

        return services;
    }
}
