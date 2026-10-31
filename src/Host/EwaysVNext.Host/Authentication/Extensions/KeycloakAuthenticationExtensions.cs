using EwaysVNext.Presentation.Endpoint.Security;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Refit;

using EwaysVNext.Outcomes.Extensions;

namespace EwaysVNext.Host.Authentication.Extensions;

public static class KeycloakAuthenticationExtensions
{
    private const string MultiRealmScheme = "KeycloakMultiRealm";
    private const string RejectScheme = "RejectUnknownIssuer";

    public static IServiceCollection AddMultiRealmKeycloakAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Keycloak").Get<KeycloakOptions>()
            ?? throw new InvalidOperationException(KeycloakAuthenticationExtensionsMessages.MissingConfigurationSection);
        options.Validate();
        services.AddSingleton(options);

        var documentRetriever = new HostRewritingDocumentRetriever(options.ExternalUrl, options.InternalUrl);
        var configManagers = new Dictionary<string, IConfigurationManager<OpenIdConnectConfiguration>>(StringComparer.Ordinal);
        var issuerToTenant = new Dictionary<string, string>(StringComparer.Ordinal);

        var authBuilder = services.AddAuthentication(auth =>
        {
            auth.DefaultScheme = MultiRealmScheme;
            auth.DefaultChallengeScheme = MultiRealmScheme;
            auth.DefaultAuthenticateScheme = MultiRealmScheme;
        });

        foreach (var (tenant, realm) in options.Realms)
        {
            var issuerUrl = options.IssuerUrl(realm);
            issuerToTenant[issuerUrl] = tenant;

            var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                options.DiscoveryUrl(realm),
                new OpenIdConnectConfigurationRetriever(),
                documentRetriever);
            configManagers[tenant] = configManager;

            authBuilder.AddJwtBearer(tenant, jwt =>
            {
                jwt.RequireHttpsMetadata = false;
                jwt.MapInboundClaims = false;
                jwt.ConfigurationManager = configManager;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuerUrl,
                    ValidateAudience = realm.Audience is not null,
                    ValidAudience = realm.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context => ValidateKeycloakAccessToken(context, tenant, realm),
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("EwaysVNext.Host.Authentication");
                        logger.LogWarning(
                            context.Exception,
                            KeycloakAuthenticationExtensionsMessages.JwtValidationFailed,
                            tenant, context.Exception.GetType().Name);
                        return Task.CompletedTask;
                    },
                };
            });
        }

        authBuilder.AddPolicyScheme(MultiRealmScheme, "Keycloak Multi-Realm Router", policy =>
        {
            policy.ForwardDefaultSelector = context => SelectTenantScheme(context, issuerToTenant);
        });

        authBuilder.AddScheme<AuthenticationSchemeOptions, RejectUnknownIssuerHandler>(RejectScheme, _ => { });

        services.AddAuthorization(authz =>
        {
            authz.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

            foreach (var (tenant, realm) in options.Realms)
            {
                authz.AddPolicy(tenant, policy =>
                {
                    policy.RequireClaim(KeycloakClaimTypes.Tenant, tenant);
                    if (realm.RequiredRole is not null)
                        policy.RequireRole(realm.RequiredRole);
                });
            }
        });

        services.AddSingleton<IReadOnlyDictionary<string, IConfigurationManager<OpenIdConnectConfiguration>>>(configManagers);
        services.AddHealthChecks()
            .AddCheck<KeycloakReadinessHealthCheck>("keycloak", tags: ["ready"]);

        return services;
    }

    private static string SelectTenantScheme(HttpContext context, IReadOnlyDictionary<string, string> issuerToTenant)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("EwaysVNext.Host.Authentication");
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Routing rejected: missing or malformed Authorization header (length {HeaderLength}).", header.Length);
            return RejectScheme;
        }

        var token = header[prefix.Length..].Trim();
        if (token.Length == 0)
        {
            logger.LogInformation(KeycloakAuthenticationExtensionsMessages.RoutingRejectedEmptyToken);
            return RejectScheme;
        }

        // Checked before the JWT handler ever sees it. A partner key is not a JWT, so letting it
        // reach CanReadToken would route it to the reject scheme — and would put a live
        // credential through a code path that logs about tokens it cannot parse.
        //
        // Goes through AuthSchemes.IsPartnerKey rather than re-spelling the prefix test, so this
        // router and the integration fixture's router share one implementation. They did not
        // before, which left this branch provable only by the suite that replaces it.
        if (AuthSchemes.IsPartnerKey(context.Request))
            return AuthSchemes.PartnerApiKey;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                logger.LogInformation(KeycloakAuthenticationExtensionsMessages.RoutingRejectedUnparseableToken);
                return RejectScheme;
            }

            var issuer = handler.ReadJwtToken(token).Issuer;
            if (issuer is not null && issuerToTenant.TryGetValue(issuer, out var tenant))
                return tenant;

            logger.LogInformation(
                "Routing rejected: unknown issuer {Issuer}; known issuers are {KnownIssuers}.",
                issuer,
                string.Join(", ", issuerToTenant.Keys));
            return RejectScheme;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, KeycloakAuthenticationExtensionsMessages.RoutingRejectedException);
            return RejectScheme;
        }
    }

    private static Task ValidateKeycloakAccessToken(TokenValidatedContext context, string tenant, RealmTenantOptions realm)
    {
        var principal = context.Principal!;
        var identity = (ClaimsIdentity)principal.Identity!;

        var tokenType = principal.FindFirst("typ")?.Value;
        if (!string.Equals(tokenType, "Bearer", StringComparison.Ordinal))
        {
            context.Fail(KeycloakAuthenticationExtensionsMessages.NotAKeycloakAccessToken);
            return Task.CompletedTask;
        }

        var authorizedParty = principal.FindFirst("azp")?.Value;
        if (!string.Equals(authorizedParty, realm.ClientId, StringComparison.Ordinal))
        {
            context.Fail(KeycloakAuthenticationExtensionsMessages.UnexpectedClient);
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value))
        {
            context.Fail(KeycloakAuthenticationExtensionsMessages.MissingSubject);
            return Task.CompletedTask;
        }

        var realmAccessJson = principal.FindFirst("realm_access")?.Value;
        if (!string.IsNullOrEmpty(realmAccessJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(realmAccessJson);
                if (doc.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
                {
                    foreach (var role in roles.EnumerateArray())
                    {
                        var roleName = role.GetString();
                        if (!string.IsNullOrEmpty(roleName))
                            identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
                    }
                }
            }
            catch (JsonException)
            {
                context.Fail(KeycloakAuthenticationExtensionsMessages.MalformedRealmAccessClaim);
                return Task.CompletedTask;
            }
        }

        if (realm.RequiredRole is not null && !identity.HasClaim(ClaimTypes.Role, realm.RequiredRole))
        {
            context.Fail(MessageFormat.Format(KeycloakAuthenticationExtensionsMessages.MissingRequiredRoleFormat, realm.RequiredRole, tenant));
            return Task.CompletedTask;
        }

        identity.AddClaim(new Claim(KeycloakClaimTypes.Tenant, tenant));
        return Task.CompletedTask;
    }
}
