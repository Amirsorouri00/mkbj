using Mkbj.Modules.Partners.Adapter.Outbound.Persistence;
using Mkbj.Modules.Partners.Adapter.Outbound.Usage;
using Mkbj.Modules.Partners.Application.Partners.Ports.Inbound;
using Mkbj.Modules.Partners.Application.Partners.Ports.Outbound;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Partners.Adapter.Outbound;

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
