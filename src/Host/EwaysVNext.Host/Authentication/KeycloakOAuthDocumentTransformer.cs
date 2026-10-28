using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using EwaysVNext.Outcomes.Extensions;

namespace EwaysVNext.Host.Authentication;

public sealed class KeycloakOAuthDocumentTransformer(KeycloakOptions options) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        foreach (var (tenant, realm) in options.Realms)
        {
            document.Components.SecuritySchemes[tenant] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = MessageFormat.Format(KeycloakOAuthDocumentTransformerMessages.SchemeDescriptionFormat, tenant),
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri($"{options.ExternalUrl}/realms/{realm.Realm}/protocol/openid-connect/auth"),
                        TokenUrl = new Uri($"{options.ExternalUrl}/realms/{realm.Realm}/protocol/openid-connect/token"),
                        Scopes = new Dictionary<string, string> { ["openid"] = KeycloakOAuthDocumentTransformerMessages.OpenIdScopeDescription },
                    },
                },
            };
        }

        return Task.CompletedTask;
    }
}
