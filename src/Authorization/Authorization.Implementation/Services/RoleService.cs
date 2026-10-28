using AuthorizationService.Application.Abstractions;
using AuthorizationService.Application.Entities;
using AuthorizationService.Application.Roles;
using AuthorizationService.Application.Roles.Requests;
using AuthorizationService.Application.Roles.Responses;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;
using EwaysVNext.Outcomes.Extensions;

namespace AuthorizationService.Implementation.Services;

public class RoleService(IApplicationDbContext db) : IRoleService
{
    public async Task<Result<RoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<RoleDto>(new Error("VALIDATION_FAILED", RoleServiceMessages.CodeAndNameRequired));
        }

        var code = request.Code.Trim();

        if (await db.Roles.AnyAsync(r => r.Code == code, cancellationToken))
        {
            return Result.Failure<RoleDto>(new Error("ROLE_CODE_TAKEN", MessageFormat.Format(RoleServiceMessages.CodeTakenFormat, code)));
        }

        var role = Role.Create(code, request.Name.Trim(), request.Rank);

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(role));
    }

    public async Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<RoleDto>(new Error("VALIDATION_FAILED", RoleServiceMessages.NameRequired));
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RoleDto>(new Error("ROLE_NOT_FOUND", MessageFormat.Format(RoleServiceMessages.NotFoundFormat, id)));
        }

        role.Update(request.Name.Trim(), request.Rank);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(role));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Result.Failure(new Error("ROLE_NOT_FOUND", MessageFormat.Format(RoleServiceMessages.NotFoundFormat, id)));
        }

        if (await db.PermissionRules.AnyAsync(r => r.RoleId == id, cancellationToken)
            || await db.UserRoleScopes.AnyAsync(a => a.RoleId == id, cancellationToken))
        {
            return Result.Failure(new Error("ROLE_IN_USE", MessageFormat.Format(RoleServiceMessages.InUseFormat, id), 409));
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<RoleDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await db.Roles.Where(r => r.Id == id).Select(ToDtoExpression).FirstOrDefaultAsync(cancellationToken);
        return dto is not null
            ? Result.Success(dto)
            : Result.Failure<RoleDto>(new Error("ROLE_NOT_FOUND", MessageFormat.Format(RoleServiceMessages.NotFoundFormat, id)));
    }

    public async Task<Result<RoleDto>> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var dto = await db.Roles.Where(r => r.Code == code).Select(ToDtoExpression).FirstOrDefaultAsync(cancellationToken);
        return dto is not null
            ? Result.Success(dto)
            : Result.Failure<RoleDto>(new Error("ROLE_NOT_FOUND", MessageFormat.Format(RoleServiceMessages.NotFoundFormat, code)));
    }

    public async Task<Result<IReadOnlyList<RoleDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var list = await db.Roles.OrderByDescending(r => r.Rank).Select(ToDtoExpression).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<RoleDto>>(list);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Role, RoleDto>> ToDtoExpression =
        r => new RoleDto(r.Id, r.Code, r.Name, r.Rank);

    private static RoleDto ToDto(Role role) => new(role.Id, role.Code, role.Name, role.Rank);
}
