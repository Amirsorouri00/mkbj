using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Crawling;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Files;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Jobs;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Search;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Persistence;
using EwaysVNext.Jobs;
using EwaysVNext.Jobs.Implementation;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddSourcingOutboundAdapters(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CrawlerOptions>().Bind(configuration.GetSection(CrawlerOptions.SectionName));
        services.AddOptions<BraveOptions>().Bind(configuration.GetSection(BraveOptions.SectionName));

        services.AddScoped<ISearchProvider, BraveSearchProvider>();

        services.AddHttpClient(BraveSearchProvider.ClientName, (provider, client) =>
        {
            var brave = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<BraveOptions>>().Value;

            client.BaseAddress = new Uri(brave.BaseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            if (!string.IsNullOrWhiteSpace(brave.ApiKey))
                client.DefaultRequestHeaders.Add("X-Subscription-Token", brave.ApiKey);
        });

        // The handler refuses private addresses before a connection is opened. Registered on the
        // named client so nothing can fetch a crawl URL without going through it.
        services.AddHttpClient(WebsiteCrawlerAdapter.ClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<CrawlerOptions>>().Value;

            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        })
        .ConfigurePrimaryHttpMessageHandler(provider =>
        {
            var options = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<CrawlerOptions>>().Value;

            return new SafeHttpHandler(options.AllowPrivateNetworks)
            {
                InnerHandler = new HttpClientHandler
                {
                    // A redirect to a private address would walk straight around the guard.
                    AllowAutoRedirect = false,
                },
            };
        });

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

        // One adapter per mode; the pipeline picks by asking each in turn.
        services.AddScoped<ISourceAdapter, FileImportAdapter>();
        services.AddScoped<ISourceAdapter, WebsiteCrawlerAdapter>();

        // Search is registered last and takes the crawlers as a dependency, because a hit is
        // read through the crawl path — which keeps robots.txt, the rights policy and the
        // address check applying to a page found by searching.
        services.AddScoped<ISourceAdapter>(provider => new WebSearchSourceAdapter(
            provider.GetRequiredService<ISearchProvider>(),
            [
                ActivatorUtilities.CreateInstance<WebsiteCrawlerAdapter>(provider),
            ],
            provider.GetRequiredService<ILogger<WebSearchSourceAdapter>>()));

        // The gate outlives a request: politeness is owed per host across every job.
        services.AddSingleton<PolitenessGate>();

        services.AddScoped<IImportStepScheduler, ImportStepScheduler>();
        services.AddJobHandler<ImportJobStepHandler, ImportJobStepPayload>();

        return services;
    }
}
