using AuthorizationService.Application.Abstractions;
using AuthorizationService.Application.Entities;
using AuthorizationService.Application.UserRoleScopes;
using AuthorizationService.Application.UserRoleScopes.Requests;
using AuthorizationService.Application.UserRoleScopes.Responses;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;
using EwaysVNext.Outcomes.Extensions;

namespace AuthorizationService.Implementation.Services;

public class UserRoleScopeService(IApplicationDbContext db) : IUserRoleScopeService
{
    public async Task<Result<UserRoleScopeDto>> BootstrapAdminAsync(Guid userId, Guid scopeId, CancellationToken cancellationToken = default)
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s => s.Id == scopeId, cancellationToken);
        if (scope is null)
        {
            return Result.Failure<UserRoleScopeDto>(new Error("VALIDATION_FAILED", MessageFormat.Format(UserRoleScopeServiceMessages.UnknownScopeFormat, scopeId)));
        }

        var topRole = await db.Roles.OrderByDescending(r => r.Rank).FirstOrDefaultAsync(cancellationToken);
        if (topRole is null)
        {
            return Result.Failure<UserRoleScopeDto>(new Error("NO_ROLES_EXIST", UserRoleScopeServiceMessages.NoRolesExist, 409));
        }

        if (await db.UserRoleScopes.AnyAsync(a => a.RoleId == topRole.Id, cancellationToken))
        {
            return Result.Failure<UserRoleScopeDto>(new Error(
                "ADMIN_ALREADY_EXISTS",
                MessageFormat.Format(UserRoleScopeServiceMessages.AdminAlreadyExistsFormat, topRole.Code),
                409));
        }

        var assignment = new UserRoleScope
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = topRole.Id,
            ScopeId = scope.Id,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.UserRoleScopes.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(assignment, topRole.Code, scope.Code));
    }

    public async Task<Result<UserRoleScopeDto>> AssignAsync(AssignUserRoleScopeRequest request, CancellationToken cancellationToken = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<UserRoleScopeDto>(new Error("VALIDATION_FAILED", MessageFormat.Format(UserRoleScopeServiceMessages.UnknownRoleFormat, request.RoleId)));
        }

        var scope = await db.Scopes.FirstOrDefaultAsync(s => s.Id == request.ScopeId, cancellationToken);
        if (scope is null)
        {
            return Result.Failure<UserRoleScopeDto>(new Error("VALIDATION_FAILED", MessageFormat.Format(UserRoleScopeServiceMessages.UnknownScopeFormat, request.ScopeId)));
        }

        var alreadyExists = await db.UserRoleScopes.AnyAsync(
            a => a.UserId == request.UserId && a.RoleId == request.RoleId && a.ScopeId == request.ScopeId,
            cancellationToken);

        if (alreadyExists)
        {
            return Result.Failure<UserRoleScopeDto>(new Error("ASSIGNMENT_TAKEN", UserRoleScopeServiceMessages.AssignmentTaken));
        }

        var assignment = new UserRoleScope
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            RoleId = role.Id,
            ScopeId = scope.Id,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.UserRoleScopes.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(assignment, role.Code, scope.Code));
    }

    public async Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var assignment = await db.UserRoleScopes.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (assignment is null)
        {
            return Result.Failure(new Error("ASSIGNMENT_NOT_FOUND", MessageFormat.Format(UserRoleScopeServiceMessages.AssignmentNotFoundFormat, id)));
        }

        db.UserRoleScopes.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<UserRoleScopeDto>>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var list = await db.UserRoleScopes
            .Where(a => a.UserId == userId)
            .Select(a => new UserRoleScopeDto(a.Id, a.UserId, a.RoleId, a.Role.Code, a.ScopeId, a.Scope.Code, a.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<UserRoleScopeDto>>(list);
    }

    private static UserRoleScopeDto ToDto(UserRoleScope assignment, string roleCode, string scopeCode)
        => new(assignment.Id, assignment.UserId, assignment.RoleId, roleCode, assignment.ScopeId, scopeCode, assignment.CreatedAtUtc);
}
