using EwaysVNext.Modules.Partners.Adapter.Inbound.Rest.Authentication;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Partners.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddPartnersRestAdapter(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, PartnerApiKeyAuthenticationHandler>(
                AuthSchemes.PartnerApiKey, _ => { });

        return services;
    }
}
