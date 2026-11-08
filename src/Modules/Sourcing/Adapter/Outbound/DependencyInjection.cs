using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Files;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Gateways;
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

        services.AddScoped<ICatalogMatcher, CatalogMatcherGateway>();
        services.AddScoped<ICatalogPublisher, CatalogPublishingGateway>();
        services.AddScoped<IPricePublisher, PricePublishingGateway>();

        // One reader per format; the dispatcher picks by asking each in turn.
        services.AddScoped<IImportFileReader, CsvImportReader>();
        services.AddScoped<IImportFileReader, JsonImportReader>();

        return services;
    }
}
