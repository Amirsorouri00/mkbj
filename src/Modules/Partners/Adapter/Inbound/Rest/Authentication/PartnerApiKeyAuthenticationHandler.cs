using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;

using EwaysVNext.Core.Application.Security;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace EwaysVNext.Modules.Partners.Adapter.Inbound.Rest.Authentication;

/// <summary>
/// Turns a <c>Bearer pk_…</c> header into a partner principal.
/// <para>
/// It returns <see cref="AuthenticateResult.NoResult"/> rather than a failure when the header is
/// absent or is not a partner key, so a request carrying a human JWT falls through to the JWT
/// scheme untouched instead of being reported as a failed partner authentication.
/// </para>
/// <para>
/// The presented key is never logged or echoed — not in a failure message, not in a trace. A log
/// line containing a live credential outlives the request that produced it.
/// </para>
/// </summary>
public sealed class PartnerApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IApiKeyAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers[HeaderNames.Authorization].ToString();

        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var presented = header[BearerPrefix.Length..].Trim();

        if (!presented.StartsWith(AuthSchemes.PartnerKeyPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var result = await authenticator.AuthenticateAsync(presented, Context.RequestAborted);

        // One message for every reason. Which check failed is in the authenticator's log, not in
        // anything the caller can observe.
        if (result is null)
            return AuthenticateResult.Fail("Invalid API key.");

        var claims = new List<Claim>
        {
            new(EwaysClaimTypes.PartnerId, result.PartnerId.ToString(CultureInfo.InvariantCulture)),
            new(EwaysClaimTypes.ApiKeyId, result.ApiKeyId.ToString(CultureInfo.InvariantCulture)),
            new(EwaysClaimTypes.RateLimitPerMinute, result.RateLimitPerMinute.ToString(CultureInfo.InvariantCulture)),
            new(EwaysClaimTypes.BulkMaxItems, result.BulkMaxItems.ToString(CultureInfo.InvariantCulture)),
        };

        claims.AddRange(result.Scopes.Select(scope => new Claim(EwaysClaimTypes.Scope, scope)));

        var identity = new ClaimsIdentity(claims, AuthSchemes.PartnerApiKey);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
