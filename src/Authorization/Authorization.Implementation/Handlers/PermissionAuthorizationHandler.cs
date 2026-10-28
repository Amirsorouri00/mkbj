using System.Security.Claims;
using AuthorizationService.Abstractions.Authorization;
using AuthorizationService.Abstractions.Authorization.Requests;
using AuthorizationService.Abstractions.Requirements;
using Microsoft.AspNetCore.Authorization;

namespace AuthorizationService.Implementation.Handlers;

public sealed class PermissionAuthorizationHandler(IAuthorizationCheckService checkService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        var result = await checkService.CheckAsync(
            new CheckAuthorizationRequest(userId, requirement.PermissionCode, ScopeId: null));

        if (result.Allowed)
        {
            context.Succeed(requirement);
        }
    }
}
