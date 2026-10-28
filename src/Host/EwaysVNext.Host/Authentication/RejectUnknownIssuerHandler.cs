using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Host.Authentication;

public sealed class RejectUnknownIssuerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var hasAuthorizationHeader = Request.Headers.ContainsKey("Authorization");
        return Task.FromResult(hasAuthorizationHeader
            ? AuthenticateResult.Fail(RejectUnknownIssuerHandlerMessages.UnknownIssuer)
            : AuthenticateResult.NoResult());
    }
}
