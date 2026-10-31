using EwaysVNext.Outcomes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace EwaysVNext.Presentation.Endpoint.Security;

/// <summary>
/// Gives a refused request the same body shape as every other response.
/// <para>
/// Without this, authorization failures are the one case ASP.NET Core answers itself: an empty
/// 401 or 403 with no body at all. A client written against this API would have exactly two
/// responses it could not parse, and both of them are the ones that most need explaining.
/// </para>
/// </summary>
public sealed class EwaysAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly AuthorizationMiddlewareResultHandler Default = new();

    private static readonly Error InsufficientScope = new(
        PartnerScopeAuthorizationHandler.InsufficientScope,
        "The API key does not grant the scope this endpoint requires.",
        StatusCodes.Status403Forbidden);

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Succeeded)
        {
            await Default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        if (authorizeResult.Challenged)
        {
            // Says which scheme to use; without it a 401 does not tell a client how to retry.
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await Write(context, Error.Unauthorized);
            return;
        }

        var error = authorizeResult.AuthorizationFailure?.FailureReasons
            .Any(reason => reason.Message == PartnerScopeAuthorizationHandler.InsufficientScope) == true
            ? InsufficientScope
            : Error.Forbidden;

        await Write(context, error);
    }

    private static Task Write(HttpContext context, Error error) =>
        Result.Failure(error).ToHttpResult().ExecuteAsync(context);
}
