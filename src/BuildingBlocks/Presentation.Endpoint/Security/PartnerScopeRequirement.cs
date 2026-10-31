using EwaysVNext.Core.Application;
using Microsoft.AspNetCore.Authorization;

namespace EwaysVNext.Presentation.Endpoint.Security;

/// <summary>One partner scope an endpoint requires.</summary>
public sealed record PartnerScopeRequirement(string Scope) : IAuthorizationRequirement;

/// <summary>
/// Checks the scope against the principal the API-key handler built — never against anything in
/// the request. The failure carries a named reason so the result handler can answer
/// <c>INSUFFICIENT_SCOPE</c> rather than a bare 403: a partner holding a valid key needs to know
/// their key is too narrow, not merely that they were refused.
/// </summary>
public sealed class PartnerScopeAuthorizationHandler : AuthorizationHandler<PartnerScopeRequirement>
{
    public const string InsufficientScope = "INSUFFICIENT_SCOPE";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PartnerScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (PartnerPrincipal.TryFrom(context.User, out var principal)
            && principal!.Has(requirement.Scope))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, InsufficientScope));
        }

        return Task.CompletedTask;
    }
}
