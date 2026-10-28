using AuthorizationService.Application.Abstractions;
using AuthorizationService.Application.Entities;
using AuthorizationService.Application.Permissions;
using AuthorizationService.Application.Permissions.Requests;
using AuthorizationService.Application.Permissions.Responses;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;
using EwaysVNext.Outcomes.Extensions;

namespace AuthorizationService.Implementation.Services;

public class PermissionService(IApplicationDbContext db) : IPermissionService
{
    public async Task<Result<PermissionDto>> CreateAsync(CreatePermissionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<PermissionDto>(new Error("VALIDATION", PermissionServiceMessages.CodeAndNameRequired));
        }

        var code = request.Code.Trim();

        if (await db.Permissions.AnyAsync(p => p.Code == code, cancellationToken))
        {
            return Result.Failure<PermissionDto>(new Error("PERMISSION_CODE_TAKEN", MessageFormat.Format(PermissionServiceMessages.CodeTakenFormat, code)));
        }

        var ruleInputsResult = await ToRuleInputsAsync(request.Rules, cancellationToken);
        if (ruleInputsResult.IsFailure)
        {
            return Result.Failure<PermissionDto>(ruleInputsResult.Error!);
        }

        Permission permission;
        try
        {
            permission = Permission.Create(code, request.Name.Trim(), request.Description, ruleInputsResult.Value!);
        }
        catch (InvalidPermissionRuleException ex)
        {
            return Result.Failure<PermissionDto>(new Error("VALIDATION", ex.Message));
        }

        db.Permissions.Add(permission);
        await db.SaveChangesAsync(cancellationToken);

        var dto = await LoadDtoAsync(permission.Id, cancellationToken);
        return dto is not null
            ? Result.Success(dto)
            : Result.Failure<PermissionDto>(new Error("INTERNAL", PermissionServiceMessages.NotPersisted));
    }

    public async Task<Result<PermissionDto>> UpdateAsync(Guid id, UpdatePermissionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<PermissionDto>(new Error("VALIDATION", PermissionServiceMessages.NameRequired));
        }

        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (permission is null)
        {
            return Result.Failure<PermissionDto>(new Error("PERMISSION_NOT_FOUND", MessageFormat.Format(PermissionServiceMessages.NotFoundFormat, id)));
        }

        var ruleInputsResult = await ToRuleInputsAsync(request.Rules, cancellationToken);
        if (ruleInputsResult.IsFailure)
        {
            return Result.Failure<PermissionDto>(ruleInputsResult.Error!);
        }

        await db.PermissionRules.Where(r => r.PermissionId == id).ExecuteDeleteAsync(cancellationToken);

        try
        {
            permission.Update(request.Name.Trim(), request.Description, ruleInputsResult.Value!);
        }
        catch (InvalidPermissionRuleException ex)
        {
            return Result.Failure<PermissionDto>(new Error("VALIDATION", ex.Message));
        }

        db.PermissionRules.AddRange(permission.Rules);

        await db.SaveChangesAsync(cancellationToken);

        var dto = await LoadDtoAsync(permission.Id, cancellationToken);
        return dto is not null
            ? Result.Success(dto)
            : Result.Failure<PermissionDto>(new Error("INTERNAL", PermissionServiceMessages.NotPersisted));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (permission is null)
        {
            return Result.Failure(new Error("PERMISSION_NOT_FOUND", MessageFormat.Format(PermissionServiceMessages.NotFoundFormat, id)));
        }

        db.Permissions.Remove(permission);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<PermissionDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await LoadDtoAsync(id, cancellationToken);
        return dto is not null
            ? Result.Success(dto)
            : Result.Failure<PermissionDto>(new Error("PERMISSION_NOT_FOUND", MessageFormat.Format(PermissionServiceMessages.NotFoundFormat, id)));
    }

    public async Task<Result<PermissionDto>> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var dto = await ProjectToDto(db.Permissions.Where(p => p.Code == code)).FirstOrDefaultAsync(cancellationToken);
        return dto is not null
            ? Result.Success(dto)
            : Result.Failure<PermissionDto>(new Error("PERMISSION_NOT_FOUND", MessageFormat.Format(PermissionServiceMessages.NotFoundFormat, code)));
    }

    public async Task<Result<IReadOnlyList<PermissionDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var list = await ProjectToDto(db.Permissions.OrderBy(p => p.Code)).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<PermissionDto>>(list);
    }

    private async Task<Result<List<PermissionRuleInput>>> ToRuleInputsAsync(IReadOnlyList<PermissionRuleRequest> rules, CancellationToken cancellationToken)
    {
        if (rules.Count == 0)
        {
            return Result.Failure<List<PermissionRuleInput>>(new Error("VALIDATION", PermissionServiceMessages.RuleRequired));
        }

        foreach (var rule in rules)
        {
            if (rule.RoleId is not null && !await db.Roles.AnyAsync(r => r.Id == rule.RoleId, cancellationToken))
            {
                return Result.Failure<List<PermissionRuleInput>>(new Error("VALIDATION", MessageFormat.Format(PermissionServiceMessages.UnknownRoleFormat, rule.RoleId)));
            }

            foreach (var scopeId in rule.ScopeIds ?? [])
            {
                if (!await db.Scopes.AnyAsync(s => s.Id == scopeId, cancellationToken))
                {
                    return Result.Failure<List<PermissionRuleInput>>(new Error("VALIDATION", MessageFormat.Format(PermissionServiceMessages.UnknownScopeFormat, scopeId)));
                }
            }
        }

        return Result.Success(rules.Select(r => new PermissionRuleInput(r.RoleId, r.ScopeIds ?? [])).ToList());
    }

    private Task<PermissionDto?> LoadDtoAsync(Guid id, CancellationToken cancellationToken)
        => ProjectToDto(db.Permissions.Where(p => p.Id == id)).FirstOrDefaultAsync(cancellationToken);

    private static IQueryable<PermissionDto> ProjectToDto(IQueryable<Permission> query)
    {
        return query.Select(p => new PermissionDto(
            p.Id,
            p.Code,
            p.Name,
            p.Description,
            p.Rules.Select(r => new PermissionRuleDto(
                r.Id,
                r.RoleId,
                r.Role != null ? r.Role.Code : null,
                r.Scopes.Select(rs => new PermissionRuleScopeDto(rs.ScopeId, rs.Scope.Code)).ToList()))
                .ToList()));
    }
}
