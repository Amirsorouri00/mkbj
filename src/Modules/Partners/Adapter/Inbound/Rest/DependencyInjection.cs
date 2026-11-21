using Mkbj.Modules.Partners.Adapter.Inbound.Rest.Authentication;
using Mkbj.Modules.Partners.Adapter.Inbound.Rest.Admin;
using Mkbj.Presentation.Endpoint;
using Mkbj.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Partners.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddPartnersRestAdapter(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, PartnerApiKeyAuthenticationHandler>(
                AuthSchemes.PartnerApiKey, _ => { });

        // Discovery scans one assembly at a time and the Host only scans its own, so a module's
        // endpoints have to register themselves here or they are silently never mapped — which
        // surfaces as a 404 with an empty body, not as a startup error.
        services.AddEndpoints(typeof(PartnerAdminEndpoints).Assembly);

        return services;
    }
}
