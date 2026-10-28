using AuthorizationService.Abstractions.Authorization;
using AuthorizationService.Abstractions.Authorization.Requests;
using AuthorizationService.Abstractions.Authorization.Responses;
using AuthorizationService.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using EwaysVNext.Outcomes.Extensions;

namespace AuthorizationService.Implementation.Services;

public class AuthorizationCheckService(IApplicationDbContext db) : IAuthorizationCheckService
{
    public async Task<CheckAuthorizationResult> CheckAsync(CheckAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var rules = await db.PermissionRules
            .Where(r => r.Permission.Code == request.PermissionCode)
            .Select(r => new
            {
                RoleRank = r.Role != null ? (int?)r.Role.Rank : null,
                ScopeIds = r.Scopes.Select(rs => rs.ScopeId).ToList(),
            })
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            return new CheckAuthorizationResult(false, MessageFormat.Format(AuthorizationCheckServiceMessages.PermissionDoesNotExistFormat, request.PermissionCode));
        }

        var assignmentsQuery = db.UserRoleScopes.Where(a => a.UserId == request.UserId);

        if (request.ScopeId is not null)
        {
            assignmentsQuery = assignmentsQuery.Where(a => a.ScopeId == request.ScopeId);
        }

        var assignments = await assignmentsQuery
            .Select(a => new { RoleRank = a.Role.Rank, a.ScopeId })
            .ToListAsync(cancellationToken);

        // Roles the token asserts count as assignments with no scope. They therefore satisfy only
        // rules that carry no scope rows — a scoped rule still needs a real assignment naming the
        // scope, so a backoffice role can never stand in for one.
        if (request.TokenRoleCodes is { Count: > 0 } tokenRoles)
        {
            var ranks = await db.Roles
                .Where(role => tokenRoles.Contains(role.Code))
                .Select(role => role.Rank)
                .ToListAsync(cancellationToken);

            if (ranks.Count > 0 && rules.Any(r =>
                    r.ScopeIds.Count == 0
                    && (r.RoleRank is null || ranks.Any(rank => rank >= r.RoleRank))))
            {
                return new CheckAuthorizationResult(true, AuthorizationCheckServiceMessages.Granted);
            }
        }

        var allowed = assignments.Any(a => rules.Any(r =>
            (r.RoleRank is null || a.RoleRank >= r.RoleRank)
            && (r.ScopeIds.Count == 0 || r.ScopeIds.Contains(a.ScopeId))));

        return allowed
            ? new CheckAuthorizationResult(true, AuthorizationCheckServiceMessages.Granted)
            : new CheckAuthorizationResult(false, AuthorizationCheckServiceMessages.NoSatisfyingAssignment);
    }
}
