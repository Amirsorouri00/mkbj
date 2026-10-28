using EwaysVNext.Modules.Partners.Application.Partners.Keys;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Modules.Partners.Application.Partners.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Partners.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPartnersApplication(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ApiKeyOptions>().Bind(configuration.GetSection(ApiKeyOptions.SectionName));

        // Resolved eagerly rather than lazily, so a deployment with no pepper fails while the
        // host is starting instead of on the first partner request.
        services.AddSingleton(ApiKeyPepper.FromConfiguration(configuration[ApiKeyPepper.ConfigurationKey]));
        services.AddSingleton<ApiKeyHasher>();

        services.AddScoped<IPartnerAdminService, PartnerAdminService>();
        services.AddScoped<IApiKeyAuthenticator, ApiKeyAuthenticator>();

        return services;
    }
}
