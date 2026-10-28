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

        // Role claims are forwarded only from the Backoffice realm. The Shop realm is open
        // self-service registration, so a role named there means nothing here however it is
        // spelled — forwarding it would let anyone who could influence their own Shop token
        // claim backoffice authority.
        var tokenRoleCodes = context.User.HasClaim(EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm)
            ? context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()
            : null;

        var result = await checkService.CheckAsync(
            new CheckAuthorizationRequest(userId, requirement.PermissionCode, ScopeId: null, tokenRoleCodes));

        if (result.Allowed)
        {
            context.Succeed(requirement);
        }
    }
}
