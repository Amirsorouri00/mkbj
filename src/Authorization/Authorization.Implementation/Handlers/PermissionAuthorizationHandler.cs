using System.Security.Claims;
using AuthorizationService.Abstractions.Authorization;
using AuthorizationService.Abstractions.Authorization.Requests;
using AuthorizationService.Abstractions.Requirements;
using EwaysVNext.Core.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace AuthorizationService.Implementation.Handlers;

public sealed class PermissionAuthorizationHandler(IAuthorizationCheckService checkService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst(EwaysClaimTypes.Subject)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        // Shared with the GraphQL field attribute: the two enforcement paths must agree on which
        // roles count, and they have already drifted once.
        var result = await checkService.CheckAsync(
            new CheckAuthorizationRequest(
                userId, requirement.PermissionCode, ScopeId: null, BackofficeRoles.From(context.User)));

        if (result.Allowed)
        {
            context.Succeed(requirement);
        }
    }
}
