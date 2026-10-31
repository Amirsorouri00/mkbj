using EwaysVNext.Modules.Partners.Adapter.Outbound.Persistence;
using EwaysVNext.Modules.Partners.Adapter.Outbound.Usage;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Outbound;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Partners.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddPartnersOutboundAdapters(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<IPartnerRepository, PartnerRepository>();
        services.AddScoped<IApiKeyLookup, ApiKeyLookup>();

        services.AddOptions<PartnerUsageOptions>()
            .Bind(configuration.GetSection(PartnerUsageOptions.SectionName));

        // One channel for the whole process, so the middleware and the flush service share it.
        services.AddSingleton<ChannelApiKeyUsageRecorder>();
        services.AddSingleton<IApiKeyUsageRecorder>(
            provider => provider.GetRequiredService<ChannelApiKeyUsageRecorder>());

        return services;
    }
}
